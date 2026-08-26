using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private static byte[] learningCurveFontBytes;
        private static GCHandle learningCurveFontHandle;
        private static FontFamily learningCurveFontFamily;
        private static bool learningCurveLoadAttempted;
        private bool gameHintFontInitialized;

        private void InitializeGameHintFont()
        {
            if (gameHintFontInitialized || gameHint == null || gameHint.IsDisposed)
                return;
            gameHintFontInitialized = true;

            try
            {
                FontFamily family = LoadEmbeddedLearningCurve();
                if (family == null)
                {
                    AppLog.Warn("Embedded Learning Curve could not be loaded; game-switch hint kept its fallback font.");
                    return;
                }

                Font oldFont = gameHint.Font;
                gameHint.UseCompatibleTextRendering = true;
                gameHint.Font = new Font(family, 18f, FontStyle.Regular, GraphicsUnit.Point);

                if (oldFont != null)
                    oldFont.Dispose();

                gameHint.AutoSize = false;
                gameHint.AutoSize = true;
                gameHint.Invalidate();

                AppLog.Info("Game-switch hint font loaded from embedded Learning Curve resource. Family=" +
                            gameHint.Font.FontFamily.Name + ", size=" +
                            gameHint.Font.SizeInPoints.ToString("0.##") + "pt, bytes=" +
                            learningCurveFontBytes.Length + ".");
            }
            catch (Exception exception)
            {
                AppLog.Error("Embedded Learning Curve initialization failed.", exception);
            }
        }

        private static FontFamily LoadEmbeddedLearningCurve()
        {
            if (learningCurveFontFamily != null)
                return learningCurveFontFamily;
            if (learningCurveLoadAttempted)
                return null;
            learningCurveLoadAttempted = true;

            Assembly assembly = Assembly.GetExecutingAssembly();

            // Each resource chunk was Base64-encoded independently from a consecutive slice of
            // the compressed stream. Decode each chunk first, then concatenate the binary slices.
            using (MemoryStream compressedStream = new MemoryStream())
            {
                for (int i = 0; i < 7; i++)
                {
                    string resourceName = "LearningCurve.b64." + i.ToString("00");
                    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                            throw new InvalidDataException("Missing embedded font resource: " + resourceName);

                        string encodedChunk;
                        using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false))
                            encodedChunk = reader.ReadToEnd().Trim();

                        byte[] compressedChunk = Convert.FromBase64String(encodedChunk);
                        compressedStream.Write(compressedChunk, 0, compressedChunk.Length);
                    }
                }

                compressedStream.Position = 0;
                using (GZipStream gzip = new GZipStream(compressedStream, CompressionMode.Decompress, true))
                using (MemoryStream output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    learningCurveFontBytes = output.ToArray();
                }
            }

            if (learningCurveFontBytes.Length != 99196)
                throw new InvalidDataException("Embedded Learning Curve size mismatch. Expected 99196 bytes, got " +
                                               learningCurveFontBytes.Length + ".");

            learningCurveFontHandle = GCHandle.Alloc(learningCurveFontBytes, GCHandleType.Pinned);
            try
            {
                PrivateFontCollection.AddMemoryFont(
                    learningCurveFontHandle.AddrOfPinnedObject(), learningCurveFontBytes.Length);

                foreach (FontFamily family in PrivateFontCollection.Families)
                {
                    if (!string.Equals(family.Name, "Learning Curve", StringComparison.OrdinalIgnoreCase))
                        continue;
                    learningCurveFontFamily = family;
                    break;
                }

                if (learningCurveFontFamily == null)
                    throw new InvalidDataException("Embedded font loaded, but the Learning Curve family was not registered.");

                return learningCurveFontFamily;
            }
            catch
            {
                if (learningCurveFontHandle.IsAllocated)
                    learningCurveFontHandle.Free();
                learningCurveFontBytes = null;
                throw;
            }
        }
    }
}
