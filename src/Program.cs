using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AgentStatusBar
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            for (int i = 0; args != null && i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--status" || a == "-s")
                {
                    // 可选第二个参数：日志目录（回归测试用合成日志）
                    string dir = (i + 1 < args.Length && Directory.Exists(args[i + 1])) ? args[i + 1] : null;
                    return RunStatusDump(dir);
                }
                if (a == "--help" || a == "-h" || a == "/?")
                {
                    Native.TryAttachParentConsole();
                    Console.WriteLine("AgentStatusBar [选项]");
                    Console.WriteLine("  (无参数)   常驻托盘运行");
                    Console.WriteLine("  --status   在控制台输出一次当前解析到的状态后退出");
                    return 0;
                }
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "AgentStatusBar_SingleInstance", out createdNew))
            {
                if (!createdNew) return 0; // 已有实例在运行

                Native.EnablePerMonitorDpi();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // 必须在任何控件创建之前设置（WindowsFormsSynchronizationContext 会创建控件）
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
                {
                    LogException(e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    LogException(e.ExceptionObject as Exception);
                };

                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

                using (TrayApp app = new TrayApp())
                {
                    Application.Run(app);
                }
            }
            return 0;
        }

        static string ErrorLogPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
        }

        static void LogException(Exception ex)
        {
            try
            {
                File.AppendAllText(ErrorLogPath(),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " +
                    (ex != null ? ex.ToString() : "unknown") + "\r\n",
                    Encoding.UTF8);
            }
            catch { }
        }

        static int RunStatusDump(string logDir)
        {
            Native.TryAttachParentConsole();
            StatusEngine engine = new StatusEngine(logDir);
            engine.FetchTitlesNow();
            AgentStatus s = engine.Compute();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== AgentStatusBar 状态导出 ===");
            sb.AppendLine("日志目录 : " + engine.LogDir);
            sb.AppendLine(String.Format("总体状态 : {0}（今日请求 {1} · 工具调用 {2} · 错误 {3}）",
                s.StateText, s.RequestsToday, s.ToolCallsToday, s.ErrorsToday));
            sb.AppendLine(String.Format("今日用量 : 入 {0} · 出 {1} · 缓存读 {2} · 缓存写 {3}",
                s.TokensIn, s.TokensOut, s.CacheRead, s.CacheWrite));
            sb.AppendLine("活动会话 : " + s.Sessions.Count);
            sb.AppendLine(String.Format("解析诊断 : 成功 {0} 行 / 失败 {1} 行 / 已知会话 {2} 个 / seed 起始字节 {3}",
                engine.ParsedLines, engine.FailedLines, engine.SessionCount, engine.SeedStart));
            if (engine.FailedSample.Length > 0)
                sb.AppendLine("失败样例 : " + engine.FailedSample);
            DateTime now = DateTime.Now;
            foreach (SessionState x in s.Sessions)
            {
                Phase p = x.EffectivePhase(now);
                string doing = (p == Phase.ToolRunning && x.CurrentTool.Length > 0)
                    ? "正在执行 " + x.ToolDisplay + " " + Ui.Dur(now - x.ToolStart)
                    : "";
                sb.AppendLine(String.Format("  [{0}] {1,-4} {2,-10} 标题={3} 模型={4} 轮次={5} 请求={6} 工具={7} 错误={8} 最近活跃={9} {10}",
                    x.ShortId, Ui.PhaseText(p), doing, x.TitleDisplay, x.ModelShort, x.Turns, x.Requests, x.Tools, x.Errors, Ui.Ago(x.LastActivity), x.Id));
            }

            string text = sb.ToString();
            try { Console.WriteLine(text); } catch { }
            try
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "status-dump.txt"),
                    text, new UTF8Encoding(false));
            }
            catch { }
            engine.Dispose();
            return 0;
        }
    }
}
