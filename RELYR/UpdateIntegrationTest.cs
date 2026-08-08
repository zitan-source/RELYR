using System.IO;

namespace RELYR;

internal static class UpdateIntegrationTest
{
    internal static async Task<int> RunAsync(TextWriter output)
    {
        try
        {
            var update = await UpdateService.CheckAsync(new Version(0, 0, 0), CancellationToken.None);
            if (update == null)
            {
                output.WriteLine("FAIL GitHub did not return a published RELYR installer and checksum");
                return 1;
            }
            output.WriteLine($"PASS GitHub latest release metadata: v{update.VersionText}");
            string installer = await UpdateService.DownloadAndVerifyAsync(update, CancellationToken.None);
            if (!File.Exists(installer) || new FileInfo(installer).Length == 0)
            {
                output.WriteLine("FAIL verified installer was not saved");
                return 1;
            }
            output.WriteLine($"PASS downloaded installer SHA-256 verified: {Path.GetFileName(installer)}");
            try
            {
                Directory.Delete(Path.GetDirectoryName(installer)!, true);
            }
            catch { }
            output.WriteLine("UPDATE INTEGRATION TEST PASSED");
            return 0;
        }
        catch (Exception ex) { output.WriteLine("FAIL update integration: " + ex); return 1; }
    }
}
