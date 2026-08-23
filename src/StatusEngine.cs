using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AgentStatusBar
{
    public enum Phase { Thinking, ToolRunning, WaitingInput, Completed, Error, Idle, NoData }

    /// <summary>单个 ZCode 会话的运行状态（由日志事件流推导）。</summary>
    public class SessionState
    {
        public string Id;
        public string Agent = "ZCode";   // 会话所属 Agent（为多 Agent 适配预留）
        public string Model = "";
        public Phase Phase = Phase.WaitingInput; // 新会话默认等待输入（可见），闲置 15 分钟自动隐去
        public DateTime LastActivity = DateTime.MinValue;   // 本地时间
        public DateTime PhaseSince = DateTime.MinValue;
        public DateTime ToolStart = DateTime.MinValue;
        public string CurrentTool = "";
        public string LastTool = "";
        public string Workspace = "";                        // 会话项目目录（日志事件）
        public string DbTitle = "";                          // 会话标题（SQLite）
        public int Turns, Requests, Errors, Tools, BackgroundTasks;
        public DateTime LastError = DateTime.MinValue;
        // null = 主会话；"" = 子智能体会话但父未知（不展示）；其余 = 父会话 ID
        public string ParentId;
        // 轮次进行中（turn.started 后未 completed/failed）。挂起的提问/计划审批
        // 期间日志完全静默，靠它区分“等用户”与“真空闲”
        public bool TurnOpen;
        // Agent 工具调用的展示名（事件到达时算好，UI 线程只读字符串避免竞争），
        // 如 “SubAgent×2·Explore”；无活跃子任务映射时为空
        public string SubAgentSummary = "";
        // 活跃子智能体 agentId -> agentType。只在引擎线程（gate 锁内）访问；
        // UI 一律读 SubAgentSummary 快照
        public readonly Dictionary<string, string> SubAgents = new Dictionary<string, string>();

        /// <summary>展示标题：SQLite 标题 > 项目目录名 > 短 ID。</summary>
        public string TitleDisplay
        {
            get
            {
                if (DbTitle != null && DbTitle.Length > 0) return DbTitle;
                if (Workspace != null && Workspace.Length > 0)
                {
                    string w = Workspace.TrimEnd('\\', '/');
                    int i = w.LastIndexOfAny(new char[] { '\\', '/' });
                    return i >= 0 && i < w.Length - 1 ? w.Substring(i + 1) : w;
                }
                return "会话 " + ShortId;
            }
        }

        public string ShortId
        {
            get { return (Id != null && Id.Length > 10) ? Id.Substring(Id.Length - 6) : (Id ?? "?"); }
        }

        /// <summary>浅副本：Compute 里把子会话统计并入父会话时，避免污染原始计数（Compute 会被反复调用）。</summary>
        public SessionState ShallowCopy()
        {
            return new SessionState
            {
                Id = Id, Agent = Agent, Model = Model, Phase = Phase,
                LastActivity = LastActivity, PhaseSince = PhaseSince, ToolStart = ToolStart,
                CurrentTool = CurrentTool, LastTool = LastTool, Workspace = Workspace, DbTitle = DbTitle,
                Turns = Turns, Requests = Requests, Errors = Errors, Tools = Tools,
                BackgroundTasks = BackgroundTasks, LastError = LastError, ParentId = ParentId,
                TurnOpen = TurnOpen, SubAgentSummary = SubAgentSummary
            };
        }

        public string ModelShort
        {
            get
            {
                if (String.IsNullOrEmpty(Model)) return "";
                int i = Model.LastIndexOf('/');
                int j = Model.LastIndexOf(':');
                int k = Math.Max(i, j);
                return (k >= 0 && k < Model.Length - 1) ? Model.Substring(k + 1) : Model;
            }
        }

        /// <summary>工具名展示：Agent 调用换成 SubAgent 汇总，其余原样。</summary>
        public string ToolDisplay
        {
            get
            {
                if (CurrentTool != "Agent") return CurrentTool;
                return SubAgentSummary.Length > 0 ? SubAgentSummary : "SubAgent";
            }
        }

        /// <summary>结合最近活跃时间修正展示用的相位（事件流停滞时自动降级；轮次未结束时多半在等用户）。</summary>
        public Phase EffectivePhase(DateTime now)
        {
            if (Phase == Phase.Idle) return Phase.Idle;
            if (Phase == Phase.Thinking || Phase == Phase.ToolRunning)
            {
                double idle = (now - LastActivity).TotalSeconds;
                if (Phase == Phase.Thinking)
                {
                    if (idle > 90)
                    {
                        // 轮次未结束却长时间无事件：多半是提问/计划审批/权限确认在等用户
                        return TurnOpen ? Phase.WaitingInput : Phase.Idle;
                    }
                    return Phase.Thinking;
                }
                // 轮次打开时容忍长工具（构建/测试），最多显示 10 分钟
                if (idle > (TurnOpen ? 600 : 90)) return Phase.Idle;
                return Phase.ToolRunning;
            }
            if (Phase == Phase.Error)
            {
                if ((now - LastError).TotalMinutes <= 5 && (now - LastActivity).TotalMinutes <= 30) return Phase.Error;
                return Phase.Idle;
            }
            if (Phase == Phase.WaitingInput)
            {
                if (TurnOpen) return Phase.WaitingInput; // 挂起的提问/审批不淡出
                return (now - LastActivity).TotalMinutes > 15 ? Phase.Idle : Phase.WaitingInput;
            }
            return (now - LastActivity).TotalMinutes > 15 ? Phase.Idle : Phase.Completed;
        }
    }

    /// <summary>所有会话聚合成的一份快照，供 UI 渲染。</summary>
    public class AgentStatus
    {
        public Phase Overall = Phase.NoData;
        public bool Running;
        public List<SessionState> Sessions = new List<SessionState>();
        public int RequestsToday, ErrorsToday, ToolCallsToday;
        public long TokensIn, TokensOut, CacheRead, CacheWrite; // 今日用量
        public string LatestModel = "";
        public DateTime LastDataTime = DateTime.MinValue;

        public string StateText
        {
            get
            {
                switch (Overall)
                {
                    case Phase.Thinking: return "思考中";
                    case Phase.ToolRunning: return "工具执行";
                    case Phase.WaitingInput: return "等待输入";
                    case Phase.Completed: return "已完成";
                    case Phase.Error: return "出错";
                    case Phase.Idle: return "空闲";
                    default: return "无数据";
                }
            }
        }

        public SessionState FirstRunning(DateTime now)
        {
            for (int i = 0; i < Sessions.Count; i++)
            {
                Phase p = Sessions[i].EffectivePhase(now);
                if (p == Phase.ToolRunning || p == Phase.Thinking) return Sessions[i];
            }
            return null;
        }

        public string SummaryLine()
        {
            if (Overall == Phase.NoData) return "ZCode · 未检测到数据";
            if (Sessions.Count == 0) return "ZCode 空闲 · 无进行中的会话";
            DateTime now = DateTime.Now;
            SessionState run = FirstRunning(now);
            if (run != null)
            {
                string model = run.ModelShort;
                Phase p = run.EffectivePhase(now);
                if (p == Phase.ToolRunning && run.CurrentTool.Length > 0)
                    return "ZCode 运行中 · " + model + " · " + run.ToolDisplay + " " + Ui.Dur(now - run.ToolStart);
                return "ZCode 运行中 · " + model + " 思考中";
            }
            return "ZCode " + StateText + " · " + Sessions.Count + " 个会话";
        }
    }

    /// <summary>
    /// 通过 tail ~/.zcode/cli/log/zcode-YYYY-MM-DD.jsonl 维护所有会话状态。
    /// FileSystemWatcher 事件驱动 + 防抖 + 定时兜底轮询，只读取新增字节。
    /// </summary>
    public class StatusEngine : IDisposable
    {
        public readonly string LogDir;
        const int SeedBytes = 256 * 1024;          // 启动/换日时回看的字节量
        const int MaxReadChunk = 8 * 1024 * 1024;
        const int RecentHours = 12;                // 面板里展示最近 N 小时的会话

        readonly JavaScriptSerializer json;
        readonly FileSystemWatcher fsw;
        readonly System.Threading.Timer poll;
        readonly System.Threading.Timer debounce;
        readonly SynchronizationContext ui;
        readonly object gate = new object();

        readonly Dictionary<string, SessionState> sessions = new Dictionary<string, SessionState>();
        readonly Dictionary<string, string> dbTitles = new Dictionary<string, string>(); // id -> 标题
        long tokIn, tokOut, tokCacheRead, tokCacheWrite; // 今日 token 用量
        volatile bool nodeAvailable = true;
        volatile bool titleFetchInFlight;
        readonly System.Threading.Timer titleTimer;
        string currentFile;
        long position;
        byte[] leftover = new byte[0];
        int requestsToday, errorsToday, toolsToday;
        int pollCount;

        // 诊断计数（--status 模式输出）
        public long ParsedLines, FailedLines;
        public string FailedSample = "";

        /// <summary>状态发生变化（UI 线程回调；无 UI 上下文时同步调用）。</summary>
        public event Action Changed;

        public StatusEngine() : this(null) { }

        public StatusEngine(string logDirOverride)
        {
            LogDir = logDirOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".zcode", "cli", "log");

            json = new JavaScriptSerializer();
            json.MaxJsonLength = 32 * 1024 * 1024;
            ui = SynchronizationContext.Current;

            if (Directory.Exists(LogDir))
            {
                fsw = new FileSystemWatcher(LogDir, "*.jsonl");
                fsw.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime;
                fsw.InternalBufferSize = 64 * 1024;
                fsw.Changed += OnFsEvent;
                fsw.Created += OnFsEvent;
                fsw.Error += OnFsError;
                fsw.EnableRaisingEvents = true;
            }
            debounce = new System.Threading.Timer(OnDebounce, null, Timeout.Infinite, Timeout.Infinite);
            poll = new System.Threading.Timer(OnPoll, null, 1000, 2000);
            titleTimer = new System.Threading.Timer(OnTitleTimer, null, 2500, 45000);

            ReadTail(); // 启动时回看种子数据
        }

        void OnFsEvent(object state, FileSystemEventArgs e) { debounce.Change(250, Timeout.Infinite); }
        void OnFsError(object state, ErrorEventArgs e) { debounce.Change(10, Timeout.Infinite); }
        void OnDebounce(object state) { try { ReadTail(); } catch { } }

        void OnPoll(object state)
        {
            pollCount++;
            try
            {
                // 兜底：换日/事件丢失时补读；平时只做空闲相位的重算通知
                if (pollCount % 15 == 0)
                    ReadTail();
                else if (currentFile == null && TodayFileExists())
                    ReadTail();
                FireChanged();
            }
            catch { }
        }

        string TodayFileName() { return "zcode-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl"; }
        string TodayPath() { return Path.Combine(LogDir, TodayFileName()); }
        bool TodayFileExists() { return File.Exists(TodayPath()); }

        void ReadTail()
        {
            lock (gate)
            {
                string path = TodayPath();
                if (!File.Exists(path)) return;
                string name = Path.GetFileName(path);
                if (!String.Equals(currentFile, name, StringComparison.OrdinalIgnoreCase))
                {
                    // 首次打开或跨天换文件：从末尾回看一段做种子
                    currentFile = name;
                    leftover = new byte[0];
                    try { position = Math.Max(0, new FileInfo(path).Length - SeedBytes); }
                    catch { position = 0; }
                }

                bool had = false;
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        long len = fs.Length;
                        if (len < position) position = 0; // 文件被截断
                        long avail = len - position;
                        if (avail <= 0 && leftover.Length == 0) return;

                        fs.Seek(position, SeekOrigin.Begin);
                        int want = (int)Math.Min(avail, (long)MaxReadChunk);
                        byte[] buf = new byte[Math.Max(want, 1)];
                        int read = 0;
                        while (read < want)
                        {
                            int n = fs.Read(buf, read, want - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        position += read;

                        byte[] all;
                        if (leftover.Length > 0)
                        {
                            all = new byte[leftover.Length + read];
                            Buffer.BlockCopy(leftover, 0, all, 0, leftover.Length);
                            Buffer.BlockCopy(buf, 0, all, leftover.Length, read);
                        }
                        else all = buf;

                        // 只处理到最后一个换行为止的完整行，剩余字节留给下一次
                        int lastNl = -1;
                        for (int i = all.Length - 1; i >= 0; i--) if (all[i] == 10) { lastNl = i; break; }
                        int completeLen = lastNl + 1;
                        byte[] newLeftover = new byte[all.Length - completeLen];
                        Buffer.BlockCopy(all, completeLen, newLeftover, 0, newLeftover.Length);
                        leftover = newLeftover;

                        if (completeLen > 0)
                        {
                            string text = Encoding.UTF8.GetString(all, 0, completeLen);
                            string[] lines = text.Split('\n');
                            for (int i = 0; i < lines.Length; i++)
                            {
                                string line = lines[i].TrimEnd('\r');
                                if (line.Length == 0) continue;
                                if (ProcessLine(line)) had = true;
                            }
                        }
                    }
                }
                catch (IOException) { }               // 写入方短暂持锁，下次事件重试
                catch (UnauthorizedAccessException) { }

                if (had) FireChanged();
            }
        }

        bool ProcessLine(string line)
        {
            Dictionary<string, object> o = null;
            try { o = json.DeserializeObject(line) as Dictionary<string, object>; }
            catch
            {
                FailedLines++;
                if (FailedSample.Length == 0 && line.Length > 0)
                    FailedSample = line.Substring(0, Math.Min(120, line.Length));
                return false;
            }
            if (o == null) { FailedLines++; return false; }

            string ev = GetStr(o, "event");
            if (ev == null) return false;
            string sid = GetStr(o, "sessionId");
            if (sid == null || sid.Length == 0) return false; // 进程级事件 v1 不关联会话
            ParsedLines++;

            DateTime ts = ParseTs(GetStr(o, "timestamp"));
            object c;
            Dictionary<string, object> ctx = o.TryGetValue("context", out c) ? c as Dictionary<string, object> : null;

            SessionState s = GetOrAdd(sid);
            string status = GetStr(o, "status");
            bool meaningful = false;

            switch (ev)
            {
                case "turn.started":
                    s.Turns++;
                    s.TurnOpen = true;
                    SetPhase(s, Phase.Thinking, ts);
                    meaningful = true;
                    break;
                case "turn.completed":
                    // 只有模型以提问收尾（最后一轮回复以 ？/? 结束）才算“等待输入”，
                    // 否则是正常完成
                    s.TurnOpen = false;
                    SetPhase(s, LastResponseEndsWithQuestion(s.Id) ? Phase.WaitingInput : Phase.Completed, ts);
                    meaningful = true;
                    break;
                case "turn.failed":
                    s.TurnOpen = false;
                    if (status == "cancelled")
                    {
                        // 用户手动中断不算错误：轮次作废，回到已完成（会话从列表隐去）
                        SetPhase(s, Phase.Completed, ts);
                    }
                    else
                    {
                        s.Errors++; errorsToday++; s.LastError = ts;
                        SetPhase(s, Phase.Error, ts);
                    }
                    meaningful = true;
                    break;
                case "model.request.completed":
                    {
                        // 后台请求（标题生成/记忆提取/网页处理等）也发 completed，
                        // 不驱动相位，否则空闲会话会误显示“运行中”
                        string qs = ctx != null ? GetStr(ctx, "querySource") : null;
                        if (qs != null && qs != "main_turn" && qs != "subagent") break;
                        s.Requests++; requestsToday++;
                        string m = ctx != null ? GetStr(ctx, "model") : null;
                        if (m != null) s.Model = m;
                        if (s.Phase != Phase.ToolRunning) SetPhase(s, Phase.Thinking, ts);
                        meaningful = true;
                    }
                    break;
                case "model.request.failed":
                    // API 请求失败由 ZCode 自动重试（多为网络超时/取消残留），不计为错误
                    break;
                case "tool.call.started":
                    s.Tools++; toolsToday++;
                    {
                        string t = ctx != null ? GetStr(ctx, "toolName") : null;
                        if (t != null && UserInputTools.Contains(t))
                        {
                            // 提问/计划审批挂在用户侧：显示“等待输入”（轮次未结束不淡出）。
                            // 这些工具的 started/completed 常在用户操作后才一并写入日志，
                            // 等待期间的静默由 EffectivePhase 的轮次规则兜底
                            s.CurrentTool = "";
                            SetPhase(s, Phase.WaitingInput, ts);
                        }
                        else
                        {
                            s.CurrentTool = t ?? "?";
                            s.ToolStart = ts;
                            SetPhase(s, Phase.ToolRunning, ts);
                        }
                    }
                    meaningful = true;
                    break;
                case "tool.call.completed":
                case "tool.call.failed":
                    if (ev == "tool.call.failed" && status != "cancelled")
                    { s.Errors++; errorsToday++; s.LastError = ts; }
                    s.LastTool = s.CurrentTool;
                    s.CurrentTool = "";
                    SetPhase(s, Phase.Thinking, ts);
                    meaningful = true;
                    break;
                case "zcode_protocol.session_create.started":
                    {
                        string ws = ctx != null ? GetStr(ctx, "workspacePath") : null;
                        if (ws != null && ws.Length > 0) s.Workspace = ws;
                        meaningful = true; // 新会话创建立即推送 UI
                    }
                    break;
                case "session.resumed":
                    {
                        string dir = ctx != null ? GetStr(ctx, "directory") : null;
                        if (dir != null && dir.Length > 0) s.Workspace = dir;
                    }
                    SetPhase(s, Phase.WaitingInput, ts);
                    meaningful = true;
                    break;
                case "background_task.tracking.started":
                    s.BackgroundTasks++;
                    break;
                case "background_task.tracking.terminal":
                    if (s.BackgroundTasks > 0) s.BackgroundTasks--;
                    break;
                case "session.model.updated":
                    {
                        string mm = ctx != null ? GetStr(ctx, "model") : null;
                        if (mm != null) s.Model = mm;
                    }
                    break;
                case "zcode_protocol.session.resident_deactivated":
                    // ZCode 自己的空闲信号（约 10 分钟无活动把常驻运行时休眠）：
                    // 会话立即隐去，不等 15 分钟淡出；用户回来操作会重新出现。
                    // 不是交互事件，不刷新 LastActivity
                    SetPhase(s, Phase.Idle, ts);
                    break;
                case "subagent.spawned":
                case "subagent.completed":
                    {
                        // 事件的 sessionId 是父会话；context.agentId 对应子会话 sess_subagent_<agentId>
                        string aid = ctx != null ? GetStr(ctx, "agentId") : null;
                        if (aid != null && aid.Length > 0)
                        {
                            GetOrAdd("sess_subagent_" + aid).ParentId = sid;
                            if (ev == "subagent.spawned")
                            {
                                string at = ctx != null ? GetStr(ctx, "agentType") : null;
                                s.SubAgents[aid] = (at != null && at.Length > 0) ? at : "sub";
                            }
                            else s.SubAgents.Remove(aid);
                            UpdateSubAgentSummary(s);
                        }
                    }
                    break;
            }
            // 只有真实交互（轮次/主请求/工具调用/新建/恢复）才算会话活跃；
            // workspace_state、mcp、bootstrap 等进程级事件也带 sessionId，
            // 若用来刷新 LastActivity，已结束/空闲会话会永远显示“等待输入”
            if (meaningful) s.LastActivity = ts;
            return meaningful;
        }

        SessionState GetOrAdd(string id)
        {
            SessionState s;
            if (!sessions.TryGetValue(id, out s))
            {
                s = new SessionState { Id = id };
                if (id.StartsWith("sess_subagent_", StringComparison.Ordinal))
                    s.ParentId = ""; // 子智能体会话，父待 subagent.spawned 补充映射
                sessions[id] = s;
            }
            return s;
        }

        static void SetPhase(SessionState s, Phase p, DateTime ts)
        {
            if (s.Phase != p) { s.Phase = p; s.PhaseSince = ts; }
        }

        /// <summary>阻塞在用户侧的工具：调用期间会话应显示“等待输入”而非执行工具。</summary>
        static readonly HashSet<string> UserInputTools = new HashSet<string>
        {
            "AskUserQuestion", "ExitPlanMode", "EnterPlanMode"
        };

        /// <summary>由活跃子任务表刷新展示汇总（SubAgent×N·类型[+类型]）。引擎锁内调用。</summary>
        static void UpdateSubAgentSummary(SessionState s)
        {
            if (s.SubAgents.Count == 0) { s.SubAgentSummary = ""; return; }
            List<string> types = new List<string>();
            foreach (string v in s.SubAgents.Values)
            {
                string t = v == "general-purpose" ? "General" : v;
                if (t.Length > 0 && !types.Contains(t)) types.Add(t);
            }
            s.SubAgentSummary = "SubAgent×" + s.SubAgents.Count + "·" + String.Join("+", types.ToArray());
        }

        static readonly string[] TsFormats = { "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy-MM-ddTHH:mm:ssZ" };

        static DateTime ParseTs(string s)
        {
            if (String.IsNullOrEmpty(s)) return DateTime.Now;
            DateTime d;
            if (DateTime.TryParseExact(s, TsFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d))
                return d.ToLocalTime();
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d))
                return d.ToLocalTime();
            return DateTime.Now;
        }

        static string GetStr(Dictionary<string, object> o, string k)
        {
            object v;
            if (o.TryGetValue(k, out v) && v != null)
            {
                string s = v as string;
                if (s != null) return s;
                try { return Convert.ToString(v, CultureInfo.InvariantCulture); }
                catch { return null; }
            }
            return null;
        }

        static long GetLong(Dictionary<string, object> o, string k)
        {
            object v;
            if (o != null && o.TryGetValue(k, out v) && v != null && !(v is string))
            {
                try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
                catch { }
            }
            return 0;
        }

        string RolloutDir
        {
            get { return Path.Combine(Path.GetDirectoryName(LogDir), "rollout"); }
        }

        /// <summary>
        /// 读会话 model-io 文件末尾，取最后一条模型回复文本，判断是否以提问收尾（？/?）。
        /// 单条记录可能带全部对话历史而达数 MB（尾部窗口里没有完整行），因此不逐行解析，
        /// 而是定位窗口内最后一次出现的 "text":" 标记（响应文本在记录中位于巨型请求历史之后），
        /// 手工解出其后的 JSON 字符串。解析失败一律视为“非提问”。
        /// </summary>
        bool LastResponseEndsWithQuestion(string sessionId)
        {
            try
            {
                string path = Path.Combine(RolloutDir, "model-io-" + sessionId + ".jsonl");
                if (!File.Exists(path)) return false;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    int want = (int)Math.Min(len, 64 * 1024);
                    fs.Seek(len - want, SeekOrigin.Begin);
                    byte[] buf = new byte[want];
                    int read = 0;
                    while (read < want)
                    {
                        int n = fs.Read(buf, read, want - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read <= 0) return false;
                    string tail = Encoding.UTF8.GetString(buf, 0, read);

                    const string marker = "\"text\":\"";
                    int idx = tail.LastIndexOf(marker);
                    while (idx >= 0)
                    {
                        string val = ExtractJsonString(tail, idx + marker.Length).Trim();
                        if (val.Length > 0) return EndsWaitingForUser(val);
                        idx = tail.LastIndexOf(marker, idx - 1);
                    }
                    return false;
                }
            }
            catch { return false; }
        }

        /// <summary>解出 s[start] 开始的 JSON 转义字符串（到未转义的引号为止）。</summary>
        static string ExtractJsonString(string s, int start)
        {
            StringBuilder sb = new StringBuilder();
            int i = start;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\')
                {
                    if (i + 1 >= s.Length) break;
                    char e = s[i + 1];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'u':
                            if (i + 5 < s.Length)
                            {
                                int code;
                                if (Int32.TryParse(s.Substring(i + 2, 4),
                                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                    i += 4;
                                }
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                    i += 2;
                }
                else if (c == '"') break;
                else { sb.Append(c); i++; }
            }
            return sb.ToString();
        }

        /// <summary>剥掉结尾的引号/括号/空白后，是否以 ？/? 收尾。</summary>
        static bool EndsWaitingForUser(string text)
        {
            if (text == null) return false;
            int i = text.Length - 1;
            while (i >= 0)
            {
                char c = text[i];
                if (Char.IsWhiteSpace(c) ||
                    c == '"' || c == '\'' || c == '”' || c == '「' || c == '」' || c == '『' || c == '』' ||
                    c == ')' || c == '）' || c == ']' || c == '】' || c == '}' || c == '…')
                    i--;
                else break;
            }
            if (i < 0) return false;
            char last = text[i];
            return last == '？' || last == '?';
        }

        public AgentStatus Compute()
        {
            lock (gate)
            {
                AgentStatus st = new AgentStatus();
                st.RequestsToday = requestsToday;
                st.ErrorsToday = errorsToday;
                st.ToolCallsToday = toolsToday;
                st.TokensIn = tokIn;
                st.TokensOut = tokOut;
                st.CacheRead = tokCacheRead;
                st.CacheWrite = tokCacheWrite;

                DateTime now = DateTime.Now;

                // 主会话与子智能体会话分离：子会话不单独展示，统计并入主会话
                Dictionary<string, List<SessionState>> childMap = new Dictionary<string, List<SessionState>>();
                List<SessionState> recent = new List<SessionState>();
                foreach (SessionState s in sessions.Values)
                {
                    if ((now - s.LastActivity).TotalHours > RecentHours) continue;
                    if (s.ParentId != null)
                    {
                        if (s.ParentId.Length > 0 && sessions.ContainsKey(s.ParentId))
                        {
                            List<SessionState> kids;
                            if (!childMap.TryGetValue(s.ParentId, out kids))
                                childMap[s.ParentId] = kids = new List<SessionState>();
                            kids.Add(s);
                        }
                        continue; // 子会话（含父未知）不作为独立会话展示
                    }
                    recent.Add(s);
                }

                List<SessionState> merged = new List<SessionState>();
                foreach (SessionState s in recent)
                {
                    List<SessionState> kids;
                    if (!childMap.TryGetValue(s.Id, out kids)) { merged.Add(s); continue; }
                    SessionState d = s.ShallowCopy();
                    foreach (SessionState k in kids)
                    {
                        d.Requests += k.Requests;
                        d.Tools += k.Tools;
                        // 子任务在跑则父会话保持活跃（否则 Agent 工具执行超 90s 会被判空闲隐藏）
                        if (k.LastActivity > d.LastActivity) d.LastActivity = k.LastActivity;
                        if (k.LastError > d.LastError) d.LastError = k.LastError;
                        Phase kp = k.EffectivePhase(now);
                        if ((kp == Phase.Thinking || kp == Phase.ToolRunning) &&
                            d.Phase != Phase.ToolRunning && d.Phase != Phase.Thinking)
                        {
                            // 父会话自身的 Agent 工具事件缺失时，用子会话运行态顶上
                            d.Phase = Phase.ToolRunning;
                            d.PhaseSince = k.PhaseSince;
                            if (d.CurrentTool == null || d.CurrentTool.Length == 0)
                            { d.CurrentTool = "Agent"; d.ToolStart = k.ToolStart; }
                        }
                    }
                    merged.Add(d);
                }
                recent = merged;
                recent.Sort(delegate(SessionState a, SessionState b) { return b.LastActivity.CompareTo(a.LastActivity); });

                // 已完成/空闲的会话不展示（后台仍跟踪，再次 turn.started 会重新出现）
                List<SessionState> shown = new List<SessionState>();
                foreach (SessionState s in recent)
                {
                    string t;
                    if (dbTitles.TryGetValue(s.Id, out t)) s.DbTitle = t ?? "";
                    Phase ep = s.EffectivePhase(now);
                    if (ep != Phase.Completed && ep != Phase.Idle && shown.Count < 8) shown.Add(s);
                    if (s.LastActivity > st.LastDataTime)
                    {
                        st.LastDataTime = s.LastActivity;
                        st.LatestModel = s.ModelShort;
                    }
                }
                st.Sessions = shown;

                bool anyTool = false, anyThink = false, anyWait = false, errFresh = false;
                foreach (SessionState s in shown)
                {
                    Phase p = s.EffectivePhase(now);
                    if (p == Phase.ToolRunning) anyTool = true;
                    else if (p == Phase.Thinking) anyThink = true;
                    else if (p == Phase.WaitingInput) anyWait = true;
                    else if (p == Phase.Error) errFresh = true;
                }

                st.Running = anyTool || anyThink;
                if (anyTool) st.Overall = Phase.ToolRunning;
                else if (anyThink) st.Overall = Phase.Thinking;
                else if (errFresh) st.Overall = Phase.Error;
                else if (anyWait) st.Overall = Phase.WaitingInput;
                else if (shown.Count > 0) st.Overall = Phase.Idle;
                else st.Overall = recent.Count > 0 ? Phase.Idle : Phase.NoData;
                return st;
            }
        }

        public int SessionCount { get { return sessions.Count; } }
        public long SeedStart { get { return Math.Max(0, position - 0); } }

        // ---------- 会话标题（SQLite，经 node 只读查询，低频缓存） ----------

        // 注意：此脚本经 -e "..." 传入，内部字符串只能用单引号且不得嵌套单引号
        // （SQL 里的 'completed' 曾把 JS 字符串截断），过滤条件用无引号写法
        const string TitleQuery =
            "const {DatabaseSync} = require('node:sqlite');" +
            "try { const db = new DatabaseSync(process.argv[1], {readOnly:true});" +
            " const r = db.prepare('SELECT id,title FROM session ORDER BY time_updated DESC LIMIT 60').all();" +
            " const d = new Date(); d.setHours(0,0,0,0);" +
            " const u = db.prepare('SELECT SUM(input_tokens) i, SUM(output_tokens) o, SUM(cache_read_input_tokens) cr, SUM(cache_creation_input_tokens) cw FROM model_usage WHERE started_at >= ? AND input_tokens IS NOT NULL').get(d.getTime());" +
            " process.stdout.write(JSON.stringify({titles:r, tokens:u})); } catch (e) { process.stdout.write('{\"titles\":[],\"tokens\":null}'); }";

        void OnTitleTimer(object state)
        {
            if (!nodeAvailable || titleFetchInFlight) return;
            titleFetchInFlight = true;
            try { FetchTitles(); }
            finally { titleFetchInFlight = false; }
        }

        /// <summary>同步拉取一次标题（--status 调试模式用）。</summary>
        public void FetchTitlesNow()
        {
            if (!nodeAvailable) return;
            FetchTitles();
        }

        void FetchTitles()
        {
            try
            {
                string dbPath = Path.Combine(Path.GetDirectoryName(LogDir), "db", "db.sqlite");
                if (!File.Exists(dbPath)) return;

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = "node";
                psi.Arguments = "-e \"" + TitleQuery + "\" \"" + dbPath + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;

                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } return; }
                    Dictionary<string, object> root = json.DeserializeObject(output) as Dictionary<string, object>;
                    if (root == null) return;
                    lock (gate)
                    {
                        object titlesObj;
                        object[] arr = root.TryGetValue("titles", out titlesObj) ? titlesObj as object[] : null;
                        if (arr != null)
                        {
                            foreach (object o in arr)
                            {
                                Dictionary<string, object> d = o as Dictionary<string, object>;
                                if (d == null) continue;
                                string id = GetStr(d, "id");
                                string title = GetStr(d, "title");
                                if (id != null && title != null) dbTitles[id] = title;
                            }
                        }
                        Dictionary<string, object> tk;
                        object tkObj;
                        if (root.TryGetValue("tokens", out tkObj) && (tk = tkObj as Dictionary<string, object>) != null)
                        {
                            tokIn = GetLong(tk, "i");
                            tokOut = GetLong(tk, "o");
                            tokCacheRead = GetLong(tk, "cr");
                            tokCacheWrite = GetLong(tk, "cw");
                        }
                    }
                    FireChanged();
                }
            }
            catch (System.ComponentModel.Win32Exception) { nodeAvailable = false; } // node 不存在
            catch { }
            finally { titleFetchInFlight = false; }
        }

        /// <summary>放弃当前状态，从头（种子窗口）重新扫描。</summary>
        public void ForceRescan()
        {
            lock (gate)
            {
                currentFile = null;
                position = 0;
                leftover = new byte[0];
                sessions.Clear();
                requestsToday = errorsToday = toolsToday = 0;
            }
            ReadTail();
            FireChanged();
        }

        void FireChanged()
        {
            Action h = Changed;
            if (h == null) return;
            if (ui != null) ui.Post(delegate { try { h(); } catch { } }, null);
            else { try { h(); } catch { } }
        }

        public void Dispose()
        {
            if (fsw != null)
            {
                try { fsw.EnableRaisingEvents = false; } catch { }
                fsw.Dispose();
            }
            if (poll != null) poll.Dispose();
            if (debounce != null) debounce.Dispose();
            if (titleTimer != null) titleTimer.Dispose();
        }
    }
}
