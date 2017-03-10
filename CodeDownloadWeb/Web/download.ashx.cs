using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;

namespace Web
{
    /// <summary>
    /// download 的摘要说明
    /// </summary>
    public class download : IHttpHandler
    {
        static string TPL_PATH = ConfigurationManager.AppSettings["TPL_PATH"];
        static string OLD_PATH = ConfigurationManager.AppSettings["OLD_PATH"];
        static string BE_REPLACE = ConfigurationManager.AppSettings["BE_REPLACE"];

        string GOAL_WORD = "";

        public void ProcessRequest(HttpContext context)
        {
            GOAL_WORD = context.Request["name"];
            if (string.IsNullOrWhiteSpace(GOAL_WORD)) throw new InvalidOperationException("请输入项目名称");

            var response = context.Response;
            response.ContentType = "application/zip";
            response.AppendHeader("content-disposition", "attachment; filename=\"" + GOAL_WORD + ".zip\"");
            response.CacheControl = "Private";
            response.Cache.SetExpires(DateTime.Now.AddMinutes(3)); // or put a timestamp in the filename in the content-disposition

            var guid = Guid.NewGuid().ToString("N");
            var newPath = Path.Combine(OLD_PATH, guid);
            Directory.CreateDirectory(newPath);
            ExtractZipFile(TPL_PATH, newPath);
            HandleDirecotry(newPath);

            var zipPath = Path.Combine(OLD_PATH, guid + ".zip");
            CreateZip(response.OutputStream, newPath);
            DeleteDir(newPath);

            response.Flush();
            response.End();

        }

        static void DeleteDir(string dir)
        {
            foreach (var f in Directory.GetFiles(dir))
            {
                File.Delete(f);
            }
            var subDir = Directory.GetDirectories(dir);
            if (subDir.Length > 0)
            {
                foreach (var item in subDir)
                    DeleteDir(item);
            }

            Directory.Delete(dir);
        }

        public bool IsReusable
        {
            get
            {
                return false;
            }
        }

        void HandleDirecotry(string dir)
        {

            string tdir = dir;
            if (dir.Contains(BE_REPLACE))
            {
                tdir = dir.Replace(BE_REPLACE, GOAL_WORD);
                Directory.Move(dir, tdir);
            }

            foreach (var fileName in Directory.GetFiles(tdir, "*.*", SearchOption.TopDirectoryOnly)
             .Where(x => x.EndsWith(".cs") || x.EndsWith(".csproj") || x.EndsWith(".config") || x.EndsWith(".sln") || x.EndsWith(".asax") || x.EndsWith(".cshtml")))
            {
                var context = File.ReadAllText(fileName);
                context = context.Replace(BE_REPLACE, GOAL_WORD);
                File.WriteAllText(fileName, context, Encoding.UTF8);

                if (fileName.Contains(BE_REPLACE))
                {
                    File.Move(fileName, fileName.Replace(BE_REPLACE, GOAL_WORD));
                }
            }

            var subDires = Directory.GetDirectories(tdir, "*", SearchOption.TopDirectoryOnly);
            foreach (var item in subDires)
            {
                HandleDirecotry(item);
            }
        }

        public void ExtractZipFile(string archiveFilenameIn, string outFolder)
        {
            ZipFile zf = null;
            try
            {
                FileStream fs = File.OpenRead(archiveFilenameIn);
                zf = new ZipFile(fs);

                foreach (ZipEntry zipEntry in zf)
                {
                    if (!zipEntry.IsFile)
                    {
                        continue;           // Ignore directories
                    }
                    String entryFileName = zipEntry.Name;
                    // to remove the folder from the entry:- entryFileName = Path.GetFileName(entryFileName);
                    // Optionally match entrynames against a selection list here to skip as desired.
                    // The unpacked length is available in the zipEntry.Size property.

                    byte[] buffer = new byte[4096];     // 4K is optimum
                    Stream zipStream = zf.GetInputStream(zipEntry);

                    // Manipulate the output filename here as desired.
                    String fullZipToPath = Path.Combine(outFolder, entryFileName);
                    string directoryName = Path.GetDirectoryName(fullZipToPath);
                    if (directoryName.Length > 0)
                        Directory.CreateDirectory(directoryName);

                    // Unzip file in buffered chunks. This is just as fast as unpacking to a buffer the full size
                    // of the file, but does not waste memory.
                    // The "using" will close the stream even if an exception occurs.
                    using (FileStream streamWriter = File.Create(fullZipToPath))
                    {
                        StreamUtils.Copy(zipStream, streamWriter, buffer);
                    }
                }
            }
            finally
            {
                if (zf != null)
                {
                    zf.IsStreamOwner = true; // Makes close also shut the underlying stream
                    zf.Close(); // Ensure we release resources
                }
            }
        }

        // Compresses the files in the nominated folder, and creates a zip file on disk named as outPathname.
        //
        public void CreateZip(Stream steam, string folderName)
        {

            ZipOutputStream zipStream = new ZipOutputStream(steam);

            zipStream.SetLevel(3); //0-9, 9 being the highest level of compression

            // This setting will strip the leading part of the folder path in the entries, to
            // make the entries relative to the starting folder.
            // To include the full path for each entry up to the drive root, assign folderOffset = 0.
            int folderOffset = folderName.Length + (folderName.EndsWith("\\") ? 0 : 1);

            CompressFolder(folderName, zipStream, folderOffset);

            zipStream.IsStreamOwner = true; // Makes the Close also Close the underlying stream
            zipStream.Close();
        }

        // Recurses down the folder structure
        //
        private void CompressFolder(string path, ZipOutputStream zipStream, int folderOffset)
        {

            string[] files = Directory.GetFiles(path);

            foreach (string filename in files)
            {

                FileInfo fi = new FileInfo(filename);

                string entryName = filename.Substring(folderOffset); // Makes the name in zip based on the folder
                entryName = ZipEntry.CleanName(entryName); // Removes drive from name and fixes slash direction
                ZipEntry newEntry = new ZipEntry(entryName);
                newEntry.DateTime = fi.LastWriteTime; // Note the zip format stores 2 second granularity

                newEntry.Size = fi.Length;

                zipStream.PutNextEntry(newEntry);

                // Zip the file in buffered chunks
                // the "using" will close the stream even if an exception occurs
                byte[] buffer = new byte[4096];
                using (FileStream streamReader = File.OpenRead(filename))
                {
                    StreamUtils.Copy(streamReader, zipStream, buffer);
                }
                zipStream.CloseEntry();
            }
            string[] folders = Directory.GetDirectories(path);
            foreach (string folder in folders)
            {
                CompressFolder(folder, zipStream, folderOffset);
            }
        }
    }
}