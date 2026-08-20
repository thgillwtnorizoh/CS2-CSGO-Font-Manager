using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CSGO_Font_Manager
{
    internal sealed class UiFontEmbeddedFile
    {
        public string FileName { get; set; }
        public byte[] OpenTypeData { get; set; }
    }

    internal static class UiFontPackageReader
    {
        // CS2's UI font package key. The package format is protobuf-based and each
        // embedded font payload is AES-encrypted before containing OpenType data.
        private static readonly byte[] FontKey =
        {
            0x13, 0xE6, 0x21, 0x14, 0xC7, 0xFA, 0x3C, 0xB9,
            0x3E, 0x86, 0xF4, 0x76, 0xF6, 0xB3, 0x2C, 0x20,
            0x4D, 0x82, 0xA4, 0x19, 0xAF, 0xF3, 0x13, 0xAE,
            0xBB, 0xA1, 0xAF, 0x92, 0xE7, 0xA0, 0xAC, 0x8D
        };

        public static bool TryRead(string path, out List<UiFontEmbeddedFile> fonts, out string error)
        {
            fonts = new List<UiFontEmbeddedFile>();
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    throw new FileNotFoundException("UI font package was not found.", path);

                byte[] data = File.ReadAllBytes(path);
                int position = 0;
                int packageVersion = -1;

                while (position < data.Length)
                {
                    ulong tag = ReadVarint(data, ref position);
                    int fieldNumber = (int)(tag >> 3);
                    int wireType = (int)(tag & 7);

                    if (fieldNumber == 1 && wireType == 0)
                    {
                        packageVersion = checked((int)ReadVarint(data, ref position));
                    }
                    else if (fieldNumber == 2 && wireType == 2)
                    {
                        byte[] encryptedMessage = ReadLengthDelimited(data, ref position);
                        byte[] encryptedPayload = ParseEncryptedFontMessage(encryptedMessage);
                        byte[] decryptedMessage = DecryptFontPayload(encryptedPayload);
                        UiFontEmbeddedFile font = ParseFontMessage(decryptedMessage);
                        if (font != null && font.OpenTypeData != null && font.OpenTypeData.Length > 0)
                            fonts.Add(font);
                    }
                    else
                    {
                        SkipField(data, ref position, wireType);
                    }
                }

                if (packageVersion != 1)
                    throw new InvalidDataException("Unsupported UI font package version: " + packageVersion + ".");
                if (fonts.Count == 0)
                    throw new InvalidDataException("The UI font package did not contain any readable fonts.");

                return true;
            }
            catch (Exception exception)
            {
                fonts.Clear();
                error = exception.Message;
                return false;
            }
        }

        private static byte[] ParseEncryptedFontMessage(byte[] message)
        {
            int position = 0;
            byte[] encryptedPayload = null;

            while (position < message.Length)
            {
                ulong tag = ReadVarint(message, ref position);
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                if (fieldNumber == 1 && wireType == 2)
                    encryptedPayload = ReadLengthDelimited(message, ref position);
                else
                    SkipField(message, ref position, wireType);
            }

            if (encryptedPayload == null || encryptedPayload.Length <= 16)
                throw new InvalidDataException("Encrypted UI font payload is missing or truncated.");

            return encryptedPayload;
        }

        private static UiFontEmbeddedFile ParseFontMessage(byte[] message)
        {
            int position = 0;
            string fileName = null;
            byte[] fontData = null;

            while (position < message.Length)
            {
                ulong tag = ReadVarint(message, ref position);
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 7);

                if (fieldNumber == 1 && wireType == 2)
                {
                    byte[] nameBytes = ReadLengthDelimited(message, ref position);
                    fileName = Encoding.UTF8.GetString(nameBytes);
                }
                else if (fieldNumber == 2 && wireType == 2)
                {
                    fontData = ReadLengthDelimited(message, ref position);
                }
                else
                {
                    SkipField(message, ref position, wireType);
                }
            }

            if (string.IsNullOrWhiteSpace(fileName) || fontData == null || fontData.Length == 0)
                throw new InvalidDataException("Decrypted UI font entry is incomplete.");

            return new UiFontEmbeddedFile
            {
                FileName = fileName,
                OpenTypeData = fontData
            };
        }

        private static byte[] DecryptFontPayload(byte[] encrypted)
        {
            if (encrypted.Length <= 16)
                throw new InvalidDataException("Encrypted UI font payload is too short.");

            byte[] encryptedIv = new byte[16];
            Buffer.BlockCopy(encrypted, 0, encryptedIv, 0, encryptedIv.Length);

            byte[] iv;
            using (Aes aes = Aes.Create())
            {
                aes.BlockSize = 128;
                aes.KeySize = 256;
                aes.Key = FontKey;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    iv = decryptor.TransformFinalBlock(encryptedIv, 0, encryptedIv.Length);
            }

            int cipherLength = encrypted.Length - 16;
            byte[] cipher = new byte[cipherLength];
            Buffer.BlockCopy(encrypted, 16, cipher, 0, cipherLength);

            using (Aes aes = Aes.Create())
            {
                aes.BlockSize = 128;
                aes.KeySize = 256;
                aes.Key = FontKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            }
        }

        private static ulong ReadVarint(byte[] data, ref int position)
        {
            ulong result = 0;
            int shift = 0;

            while (shift < 64)
            {
                if (position >= data.Length)
                    throw new EndOfStreamException("Unexpected end of protobuf varint.");

                byte value = data[position++];
                result |= ((ulong)(value & 0x7F)) << shift;
                if ((value & 0x80) == 0) return result;
                shift += 7;
            }

            throw new InvalidDataException("Malformed protobuf varint.");
        }

        private static byte[] ReadLengthDelimited(byte[] data, ref int position)
        {
            int length = checked((int)ReadVarint(data, ref position));
            if (length < 0 || position > data.Length - length)
                throw new InvalidDataException("Length-delimited protobuf field is truncated.");

            byte[] result = new byte[length];
            Buffer.BlockCopy(data, position, result, 0, length);
            position += length;
            return result;
        }

        private static void SkipField(byte[] data, ref int position, int wireType)
        {
            switch (wireType)
            {
                case 0:
                    ReadVarint(data, ref position);
                    return;
                case 1:
                    EnsureAvailable(data, position, 8);
                    position += 8;
                    return;
                case 2:
                    int length = checked((int)ReadVarint(data, ref position));
                    EnsureAvailable(data, position, length);
                    position += length;
                    return;
                case 5:
                    EnsureAvailable(data, position, 4);
                    position += 4;
                    return;
                default:
                    throw new InvalidDataException("Unsupported protobuf wire type: " + wireType + ".");
            }
        }

        private static void EnsureAvailable(byte[] data, int position, int length)
        {
            if (length < 0 || position < 0 || position > data.Length - length)
                throw new InvalidDataException("Protobuf field extends beyond the input data.");
        }
    }
}
