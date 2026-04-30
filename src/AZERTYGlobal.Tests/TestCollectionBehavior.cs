// Désactiver le parallélisme inter-classes : ConfigManager est statique et ses
// caches sont partagés entre tests. Les tests qui réinitialisent _configPath
// via OverrideConfigPathForTests entreraient en race s'ils tournaient en parallèle.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
