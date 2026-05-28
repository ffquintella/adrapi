using System;
using System.Security.Cryptography;

namespace adrapi.domain.Security
{
    /// <summary>
    /// ChaCha20-Poly1305 authenticated encryption (RFC 8439). Wire format:
    ///   version(1) || nonce(12) || ciphertext(N) || tag(16)
    ///
    /// Symmetric AEAD with a 256-bit key. Like AES-256, considered
    /// quantum-resistant in practice — Grover's algorithm only halves the
    /// effective key strength (to 128 bits, still secure). Chosen for parity
    /// with AES-256-GCM without the dependency on AES-NI hardware.
    /// </summary>
    public static class SecretBox
    {
        private const byte Version = 1;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        public const int KeySize = 32;

        public static byte[] Encrypt(byte[] key, byte[] plaintext)
        {
            if (key == null || key.Length != KeySize) throw new ArgumentException($"key must be {KeySize} bytes", nameof(key));
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];

            using var aead = new ChaCha20Poly1305(key);
            aead.Encrypt(nonce, plaintext, ciphertext, tag);

            var output = new byte[1 + NonceSize + ciphertext.Length + TagSize];
            output[0] = Version;
            Buffer.BlockCopy(nonce, 0, output, 1, NonceSize);
            Buffer.BlockCopy(ciphertext, 0, output, 1 + NonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, output, 1 + NonceSize + ciphertext.Length, TagSize);
            return output;
        }

        public static byte[] Decrypt(byte[] key, byte[] envelope)
        {
            if (key == null || key.Length != KeySize) throw new ArgumentException($"key must be {KeySize} bytes", nameof(key));
            if (envelope == null || envelope.Length < 1 + NonceSize + TagSize)
                throw new CryptographicException("Malformed envelope.");
            if (envelope[0] != Version)
                throw new CryptographicException($"Unsupported envelope version {envelope[0]}.");

            var nonce = new byte[NonceSize];
            Buffer.BlockCopy(envelope, 1, nonce, 0, NonceSize);

            var ctLen = envelope.Length - 1 - NonceSize - TagSize;
            var ciphertext = new byte[ctLen];
            Buffer.BlockCopy(envelope, 1 + NonceSize, ciphertext, 0, ctLen);

            var tag = new byte[TagSize];
            Buffer.BlockCopy(envelope, 1 + NonceSize + ctLen, tag, 0, TagSize);

            var plaintext = new byte[ctLen];
            using var aead = new ChaCha20Poly1305(key);
            aead.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
    }
}
