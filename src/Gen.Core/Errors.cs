namespace Gen.Core;

// Pipeline aşama hataları (spec §1 hata modları). Sessiz eksik-üretim yasak.
public sealed class LoadError(string message) : Exception(message);
public sealed class JoinError(string message) : Exception(message);
public sealed class ModelError(string message) : Exception(message);
