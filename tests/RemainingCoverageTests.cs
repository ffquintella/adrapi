using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using adrapi;
using adrapi.Controllers;
using adrapi.Entra;
using adrapi.domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Novell.Directory.Ldap;
using Xunit;

namespace tests
{
    public class RemainingCoverageTests
    {
        [Fact]
        public async Task TokenProvider_MissingClientCertificateReportsTheConfiguredPath()
        {
            var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.p12");
            var config = ValidEntraConfig($"certificate-{Guid.NewGuid():N}");
            config.ClientSecret = null;
            config.CertificatePath = path;

            var error = await Assert.ThrowsAsync<WrongParameterException>(() =>
                EntraTokenProvider.Instance.AcquireTokenAsync(config));

            Assert.Contains(path, error.Message);
        }

        [Fact]
        public async Task TokenProvider_HonorsCancellationBeforeSendingClientCredentialRequest()
        {
            var config = ValidEntraConfig($"cancel-{Guid.NewGuid():N}");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                EntraTokenProvider.Instance.AcquireTokenAsync(config, cancellation.Token));
        }

        [Theory]
        [InlineData("1.13.3\n")]
        [InlineData("1.13.3\r\n")]
        [InlineData("1.13.3\r\n\n")]
        [InlineData("1.13.3")]
        public void InfosController_ReturnsListAboutVersionAndNotFound(string versionText)
        {
            var controller = new InfosController(NullLogger<InfosController>.Instance);

            Assert.Equal(new[] { "version", "about" }, controller.Get().Value);
            Assert.Equal(
                "This is the Active Directory Read API or just ADRAPI.",
                Assert.IsType<OkObjectResult>(controller.Get(info: "about").Result).Value);
            Assert.IsType<NotFoundResult>(controller.Get(info: "unknown").Result);

            var originalDirectory = Environment.CurrentDirectory;
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"adrapi-version-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            try
            {
                File.WriteAllText(Path.Combine(tempDirectory, "version.txt"), versionText);
                Environment.CurrentDirectory = tempDirectory;
                var version = Assert.IsType<OkObjectResult>(controller.Get(info: "version").Result);
                Assert.Equal("1.13.3", version.Value);
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Fact]
        public void ObjectManager_RangeParserAcceptsNumericAndTerminalWindowsAndRejectsMalformedNames()
        {
            Assert.True(ObjectManagerHarness.TryParseRange("member;range=0-1499", "member", out var start, out var end, out var terminal));
            Assert.Equal(0, start);
            Assert.Equal(1499, end);
            Assert.False(terminal);

            Assert.True(ObjectManagerHarness.TryParseRange("member;range=1500-*", "member", out start, out end, out terminal));
            Assert.Equal(1500, start);
            Assert.Equal(int.MaxValue, end);
            Assert.True(terminal);

            Assert.False(ObjectManagerHarness.TryParseRange(null, "member", out _, out _, out _));
            Assert.False(ObjectManagerHarness.TryParseRange("member", "member", out _, out _, out _));
            Assert.False(ObjectManagerHarness.TryParseRange("member;range=-5", "member", out _, out _, out _));
            Assert.False(ObjectManagerHarness.TryParseRange("member;range=x-5", "member", out _, out _, out _));
            Assert.False(ObjectManagerHarness.TryParseRange("member;range=5-x", "member", out _, out _, out _));
        }

        [Fact]
        public void ObjectManager_AttributeAndSidHelpersHandleMissingDuplicateAndInvalidData()
        {
            Assert.Empty(ObjectManagerHarness.AttributeValues(null, "member"));
            Assert.Empty(ObjectManagerHarness.AttributeValues(
                new LdapEntry("CN=A", new LdapAttributeSet()), " "));

            var entry = new LdapEntry("CN=A", new LdapAttributeSet
            {
                new LdapAttribute("member", new[] { "CN=One", "CN=ONE", "" }),
                new LdapAttribute("other", "ignored")
            });
            Assert.Equal(new[] { "CN=One" }, ObjectManagerHarness.AttributeValues(entry, "member"));

            Assert.EndsWith("-21-42", ObjectManagerHarness.ConvertSid(new byte[]
            {
                1, 5, 0, 0, 0, 0, 0, 2,
                21, 0, 0, 0,
                42, 0, 0, 0
            }));
            Assert.StartsWith("S-1-0x", ObjectManagerHarness.ConvertSid(new byte[]
            {
                1, 1, 2, 3, 4, 5, 6, 0
            }));
            Assert.Equal("", ObjectManagerHarness.ConvertSid(new byte[] { 1 }));
        }

        private static EntraConfig ValidEntraConfig(string domainKey)
            => new EntraConfig
            {
                DomainKey = domainKey,
                TenantId = "00000000-0000-0000-0000-000000000001",
                ClientId = "00000000-0000-0000-0000-000000000002",
                ClientSecret = "test-secret-value"
            };

        private sealed class ObjectManagerHarness : ObjectManager
        {
            internal static System.Collections.Generic.List<string> AttributeValues(
                LdapEntry entry,
                string attributeName)
                => GetAttributeStringValues(entry, attributeName);

            internal static bool TryParseRange(
                string attributeKey,
                string baseName,
                out int start,
                out int end,
                out bool terminal)
                => TryParseRangeAttributeName(attributeKey, baseName, out start, out end, out terminal);

            internal static string ConvertSid(byte[] value)
                => new ObjectManagerHarness().ConvertByteToStringSid(value);
        }
    }
}
