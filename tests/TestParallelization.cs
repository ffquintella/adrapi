// These tests mutate process-global singletons (ConfigurationManager.Instance,
// LdapDomainRegistry's per-domain cache, the manager singletons). Running test
// collections in parallel makes the shared state racy and order-dependent, so
// parallelization is disabled assembly-wide.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
