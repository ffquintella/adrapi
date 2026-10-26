// These tests mutate process-global singletons (ConfigurationManager.Instance,
// LdapDomainRegistry's per-domain cache, the manager singletons). Running test
// collections in parallel makes the shared state racy and order-dependent, so
// parallelization is disabled assembly-wide.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
