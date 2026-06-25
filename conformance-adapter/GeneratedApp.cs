using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ConformanceAdapter;

// Üretilen app assembly'sini izole bir AssemblyLoadContext'te yükler ve `AddGenerated` DI host'unu
// kurar. Bu, A3'teki "ADAPTER paketten / nasıl koşulur" mekanizmasının dil-özgül (C#/DI/reflection)
// gerçekleştirimidir — ASSERTION TAŞIMAZ. Yalnızca: op handler'ı resolve + ExecuteAsync çağır +
// dönen Result<T>'yi yapısal olarak (alt-tip adı + Code) açığa çıkar.
public sealed class GeneratedApp : IDisposable
{
    public Assembly Assembly { get; }
    readonly ServiceProvider _root;
    readonly string _rootNs;

    GeneratedApp(Assembly asm, ServiceProvider root, string rootNs)
    {
        Assembly = asm;
        _root = root;
        _rootNs = rootNs;
    }

    // Built App.dll yolundan yükle. rootNs = üretilen app kök namespace'i (varsayılan "App").
    public static GeneratedApp Load(string appDllPath, string rootNs = "App")
    {
        if (!File.Exists(appDllPath))
            throw new FileNotFoundException($"Üretilen app assembly bulunamadı: {appDllPath}");

        var alc = new AppLoadContext(appDllPath);
        var asm = alc.LoadFromAssemblyPath(Path.GetFullPath(appDllPath));

        var bootstrap = asm.GetType($"{rootNs}.GeneratedBootstrap")
            ?? throw new InvalidOperationException($"{rootNs}.GeneratedBootstrap bulunamadı");
        var addGenerated = bootstrap.GetMethod("AddGenerated", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("AddGenerated metodu bulunamadı");

        var services = new ServiceCollection();
        addGenerated.Invoke(null, new object[] { services });
        var sp = services.BuildServiceProvider();
        return new GeneratedApp(asm, sp, rootNs);
    }

    public IServiceScope CreateScope() => _root.CreateScope();

    // op handler'ı DI'dan resolve et. Üretilen tip adı = "{rootNs}.{module}.{opId}Handler".
    // Modül bilinmediği için tüm handler tipleri arasında ad-eşleşmesiyle bulunur.
    public object ResolveHandler(IServiceScope scope, string opId)
    {
        var handlerType = FindHandlerType(opId);
        return scope.ServiceProvider.GetService(handlerType)
            ?? throw new InvalidOperationException($"{opId}Handler DI'dan resolve edilemedi");
    }

    Type FindHandlerType(string opId)
    {
        var name = $"{opId}Handler";
        var t = Assembly.GetTypes().FirstOrDefault(x => x.Name == name && x.GetMethod("ExecuteAsync") != null)
            ?? throw new InvalidOperationException($"{name} tipi üretilen assembly'de bulunamadı");
        return t;
    }

    // act.with (dil-nötr JSON) → handler'ın beklediği request record'una çevir (param-eşleşmesi).
    // Request tipi ExecuteAsync'in ilk parametresinden okunur (CreateInvoiceCommand vb.).
    public object BuildRequest(object handler, JsonElement with)
    {
        var exec = handler.GetType().GetMethod("ExecuteAsync")
            ?? throw new InvalidOperationException("ExecuteAsync bulunamadı");
        var reqType = exec.GetParameters()[0].ParameterType;
        var ctor = reqType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

        // dil-nötr JSON anahtarlarını (camelCase) ctor parametrelerine (PascalCase) ad-eşle.
        var jsonByLower = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (with.ValueKind == JsonValueKind.Object)
            foreach (var p in with.EnumerateObject())
                jsonByLower[p.Name] = p.Value;

        var ctorParams = ctor.GetParameters();
        var argv = new object?[ctorParams.Length];
        for (var i = 0; i < ctorParams.Length; i++)
        {
            var p = ctorParams[i];
            if (jsonByLower.TryGetValue(p.Name!, out var je))
                argv[i] = Convert(je, p.ParameterType);
            else
                argv[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }
        return ctor.Invoke(argv);
    }

    static object? Convert(JsonElement je, Type target)
    {
        var nn = Nullable.GetUnderlyingType(target) ?? target;
        if (nn == typeof(string)) return je.ValueKind == JsonValueKind.Null ? null : je.GetString();
        if (nn == typeof(decimal)) return je.GetDecimal();
        if (nn == typeof(int)) return je.GetInt32();
        if (nn == typeof(long)) return je.GetInt64();
        if (nn == typeof(double)) return je.GetDouble();
        if (nn == typeof(bool)) return je.GetBoolean();
        if (nn == typeof(Guid)) return je.GetGuid();
        // fallback: hedef tipe deserialize
        return JsonSerializer.Deserialize(je.GetRawText(), target);
    }

    // ExecuteAsync(request, CancellationToken) çağır + Task<Result<T>>'yi await et + Result objesini döndür.
    public async Task<object> ActAsync(object handler, object request)
    {
        var exec = handler.GetType().GetMethod("ExecuteAsync")!;
        var task = (Task)exec.Invoke(handler, new object?[] { request, CancellationToken.None })!;
        await task.ConfigureAwait(false);
        var resultProp = task.GetType().GetProperty("Result")!;
        return resultProp.GetValue(task)!;
    }

    // Result<T> objesinden yapısal bilgiyi açığa çıkar: alt-tip adı (Success/NotProcessable/...) +
    // (varsa) Code. BU DEĞERLER ASSERT EDİLMEZ — yalnızca spec ile karşılaştırılmak üzere okunur.
    public static ResultShape Inspect(object result)
    {
        var t = result.GetType();
        // Generic alt-tip adı: "NotProcessable`1" → "NotProcessable"
        var name = t.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];

        string? code = null;
        var codeProp = t.GetProperty("Code");
        if (codeProp != null) code = codeProp.GetValue(result)?.ToString();

        return new ResultShape(name, code);
    }

    // Success<T> ise, persist edilen entity'nin verilen (PascalCase eşlenir) decimal alanını oku.
    // Success değilse / alan yoksa false. invariant property koşumu için (yapısal, assertion taşımaz).
    public static bool TryGetSuccessFieldDecimal(object result, string field, out decimal value)
    {
        value = 0m;
        var t = result.GetType();
        var name = t.Name;
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];
        if (name != "Success") return false;

        var valueObj = t.GetProperty("Value")?.GetValue(result);
        if (valueObj is null) return false;

        // camelCase alan adını PascalCase property'ye eşle.
        var pascal = char.ToUpperInvariant(field[0]) + field[1..];
        var prop = valueObj.GetType().GetProperty(pascal);
        var raw = prop?.GetValue(valueObj);
        if (raw is null) return false;

        value = System.Convert.ToDecimal(raw, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    public void Dispose() => _root.Dispose();

    // İzole yükleme: üretilen app'in deps.json'ı üzerinden bağımlılıkları çöz (EF/ASP.NET shared fw).
    sealed class AppLoadContext : AssemblyLoadContext
    {
        readonly AssemblyDependencyResolver _resolver;
        public AppLoadContext(string mainAssemblyPath)
            : base(isCollectible: false) => _resolver = new AssemblyDependencyResolver(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName name)
        {
            var path = _resolver.ResolveAssemblyToPath(name);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }
    }
}

// Result<T>'nin yapısal görünümü: alt-tip adı + (varsa) Code. Spec'in assert'iyle KARŞILAŞTIRILIR.
public sealed record ResultShape(string ResultType, string? Code);
