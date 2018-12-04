//#define DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration;

namespace PYJ_Publish
{
    class Program
    {
        // test

        #region 윈도우메세지 상수
        public const int WM_SYSCOMMAND = 0x0112;
        public const int SC_CLOSE = 0xF060;
        public const int SC_MAXIMIZE = 0xF030;
        public const int SC_MINIMIZE = 0xF020;
        public const int SC_MOVE = 0xF010;
        public const int SC_RESTORE = 0xF120;
        public const int SC_SIZE = 0xF000;
        #endregion

        #region 윈도우메세지 함수
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern int FindWindow(string lpClassName, string NoteName);
        #endregion

        private static UpdatingState _state;
        private static string newPath = "";
        private static string exeFolder = "updater";
        private static string tempFolder = "temp";
        private static string procName, uri;
        private static IntPtr handle;

        static void Main(string[] args)
        {
#if DEBUG
            args = new[] { "SisHmi", "http://128.131.136.4/file/sishmi.zip" };
#endif
            newPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location).Replace(exeFolder, string.Empty);
            handle = Process.GetCurrentProcess().MainWindowHandle;
            SendMessage(handle, WM_SYSCOMMAND, SC_MINIMIZE, 0);
            Console.Title = "PYJ 버전 관리 매니저";
            Console.CursorVisible = false;

            // [0]:타스크명, [1]:다운로드 URL
            if (args.Length != 2)
            {
                Console.WriteLine("매개변수가 잘못 되었습니다.");
                return;
            }
            //procName = args[0];
            //uri = args[1];
            procName = ConfigurationManager.AppSettings["procName"];
            uri = ConfigurationManager.AppSettings["uri"];

            // 버전 체크
            if (!NeedUpdate()) return;
            SendMessage(handle, WM_SYSCOMMAND, SC_RESTORE, 0);

            // 프로그램 종료
            try
            {
                Thread.Sleep(1000);

                if (Process.GetProcesses().Any(p => p.ProcessName == procName))
                {
                    var proc = Process.GetProcessesByName(procName).FirstOrDefault();
                    if (proc != null)
                    {
                        proc.Kill();
                        Console.WriteLine($"[{procName}]을(를) 종료합니다.");
                    }                    
                }
            }
            catch(Exception ex)
            {
                return;
            }
            
            // 프로그램 다운로드
            try
            {
                var update = Update(uri);
                update.Wait();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            // temp 폴더 삭제
            finally
            {
                try
                {
                    Console.WriteLine($"{tempFolder} 폴더 삭제....");
                    if (Directory.Exists(tempFolder))
                        Directory.Delete(tempFolder, true);
                    Console.WriteLine("완료!");
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"[{tempFolder} 폴더 삭제 실패] {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 다운로드/압축풀기
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static async Task Update(string url)
        {
            //  임시폴더 생성
            var fileName = url.Split('/').LastOrDefault() ?? "tmp.zip";
            var filePath = Path.Combine(tempFolder, fileName);
            try
            {
                Console.WriteLine($"{tempFolder} 폴더 생성");
                if (Directory.Exists(tempFolder))
                    Directory.Delete(tempFolder, true);
                Directory.CreateDirectory(tempFolder);
            }
            catch (Exception ex)
            {
                throw new Exception($"{tempFolder} 삭제/생성 실패!", ex);
            }
            // 다운로드
            _state = UpdatingState.Downloading;
            try
            {
                using (var wc = new WebClient())
                {
                    var lockThis = new object();
                    var done = false;
                    wc.DownloadProgressChanged += (sender, e) =>
                    {
                        lock (lockThis)
                        {
                            if (!done)
                            {
                                Console.CursorLeft = 0;
                                Console.CursorTop = 2;
                                Console.WriteLine($"최신버전 다운로드... {e.BytesReceived / (1024)}/{e.TotalBytesToReceive / (1024)}KB ({e.ProgressPercentage}%)");
                            }
                            if (e.ProgressPercentage == 100)
                            {
                                done = true;
                            }
                        }
                    };
                    wc.DownloadFileCompleted += Wc_DownloadFileCompleted;
                    await wc.DownloadFileTaskAsync(url, filePath);
                    var local = File.GetLastWriteTime(filePath);
                }
            }
            catch (Exception e)
            {
                throw new Exception("다운로드 실패!", e);
            }

            // 압축 풀기
            _state = UpdatingState.Extracting;            
            try
            {
                Console.WriteLine("압축 푸는 중...");
                ZipFile.ExtractToDirectory(filePath, tempFolder);
                CopyFiles(tempFolder, newPath);
            }
            catch (Exception e)
            {
                throw new Exception("압축 풀기 실패!", e);
            }
            _state = UpdatingState.Starting;
            // 대상 타스크 실행
            try
            {
                Process.Start($"{newPath}{procName}.exe");
            }
            catch (Exception e)
            {
                throw new Exception($"{procName} 실행 실패!", e);
            }
        }

        private static void Wc_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            var test = e;
        }

        private static bool NeedUpdate()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(uri);
                HttpWebResponse response = (HttpWebResponse)req.GetResponse();
                var remote = response.LastModified;

                var local = File.GetLastWriteTime($"{newPath}{procName}.zip");

                if (local < remote)
                    return true;
                else
                    return false;
                
            }
            catch (Exception)
            {
                return true;
            }



        }

        /// <summary>
        /// 파일 복사
        /// </summary>
        /// <param name="dir"></param>
        /// <param name="newPath"></param>
        private static void CopyFiles(string dir, string newPath)
        {
            // 파일 복사
            foreach (var file in Directory.GetFiles(dir))
            {
                var newDir = newPath + dir.Replace($"{tempFolder}\\", string.Empty);
                if (!Directory.Exists(newDir))
                    Directory.CreateDirectory(newDir);

                var newFilePath = newPath + file.Replace($"{tempFolder}\\", string.Empty);                
                File.Copy(file, newFilePath, true);
                Console.CursorLeft = 0;
                Console.CursorTop = 4;
                Console.WriteLine($"{newFilePath}");
            }
            // 폴더 내 파일 복사
            foreach (var subDir in Directory.GetDirectories(dir))
            {
                if (subDir == $"{tempFolder}\\{exeFolder}") continue;
                CopyFiles(subDir, newPath);
            }
        }        
    }

    public enum UpdatingState
    {
        Preparation,
        Downloading,
        Extracting,
        Starting
    }
}
