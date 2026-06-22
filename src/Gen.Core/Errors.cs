namespace Gen.Core;

// Pipeline aşama hataları (spec §1 hata modları). Sessiz eksik-üretim yasak.
public sealed class LoadError(string message) : Exception(message);
public sealed class JoinError(string message) : Exception(message);
public sealed class ModelError(string message) : Exception(message);

/// <summary>Hedef-adaptör bir construct'ı realize edemiyor (INV-7). Sessiz düşürme yerine
/// rapor edilir; çağıran build-report'a Unsupported yazar.</summary>
public sealed class UnsupportedConstruct(string message) : Exception(message);
