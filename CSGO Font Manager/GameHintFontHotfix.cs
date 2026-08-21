using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CSGO_Font_Manager
{
    public partial class Form1
    {
        private static readonly bool GameHintFontHotfixBootstrapRegistered = RegisterGameHintFontHotfixBootstrap();
        private static byte[] learningCurveFontBytes;
        private static GCHandle learningCurveFontHandle;
        private static FontFamily learningCurveFontFamily;
        private static bool learningCurveLoadAttempted;
        private bool gameHintFontHotfixApplied;

        private static bool RegisterGameHintFontHotfixBootstrap()
        {
            Application.Idle += GameHintFontHotfixOnIdle;
            return true;
        }

        private static void GameHintFontHotfixOnIdle(object sender, EventArgs e)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                Form1 form = openForm as Form1;
                if (form == null || form.IsDisposed || form.gameHintFontHotfixApplied)
                    continue;
                if (!form.dualReady || form.gameHint == null)
                    continue;

                form.gameHintFontHotfixApplied = true;

                try
                {
                    FontFamily family = LoadEmbeddedLearningCurve();
                    if (family != null)
                    {
                        Font oldFont = form.gameHint.Font;

                        // PrivateFontCollection fonts need GDI+ rendering in WinForms.
                        form.gameHint.UseCompatibleTextRendering = true;
                        form.gameHint.Font = new Font(family, 9f, FontStyle.Regular, GraphicsUnit.Point);

                        if (oldFont != null) oldFont.Dispose();

                        form.gameHint.AutoSize = false;
                        form.gameHint.AutoSize = true;
                        form.gameHint.Invalidate();

                        AppLog.Info("Game-switch hint font loaded from embedded Learning Curve resource. Family=" + form.gameHint.Font.FontFamily.Name + ", bytes=" + learningCurveFontBytes.Length + ".");
                    }
                    else
                    {
                        AppLog.Warn("Embedded Learning Curve could not be loaded; game-switch hint kept its fallback font.");
                    }
                }
                catch (Exception exception)
                {
                    AppLog.Error("Embedded Learning Curve initialization failed.", exception);
                }

                form.LayoutDualBits();
            }
        }

        private static FontFamily LoadEmbeddedLearningCurve()
        {
            if (learningCurveFontFamily != null) return learningCurveFontFamily;
            if (learningCurveLoadAttempted) return null;
            learningCurveLoadAttempted = true;

            Assembly assembly = Assembly.GetExecutingAssembly();

            // The embedded payload was split AFTER Base64 encoding each binary chunk.
            // Concatenating the Base64 text first corrupts bytes at chunk boundaries.
            // Decode each chunk independently, then join the original compressed bytes.
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
                throw new InvalidDataException("Embedded Learning Curve size mismatch. Expected 99196 bytes, got " + learningCurveFontBytes.Length + ".");

            learningCurveFontHandle = GCHandle.Alloc(learningCurveFontBytes, GCHandleType.Pinned);
            try
            {
                PrivateFontCollection.AddMemoryFont(
                    learningCurveFontHandle.AddrOfPinnedObject(), learningCurveFontBytes.Length);

                foreach (FontFamily family in PrivateFontCollection.Families)
                {
                    if (string.Equals(family.Name, "Learning Curve", StringComparison.OrdinalIgnoreCase))
                    {
                        learningCurveFontFamily = family;
                        break;
                    }
                }

                if (learningCurveFontFamily == null)
                    throw new InvalidDataException("Embedded font loaded, but the Learning Curve family was not registered.");

                return learningCurveFontFamily;
            }
            catch
            {
                if (learningCurveFontHandle.IsAllocated) learningCurveFontHandle.Free();
                learningCurveFontBytes = null;
                throw;
            }
        }
    }
}
