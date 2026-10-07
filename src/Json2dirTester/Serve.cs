using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Json2dirTester;

/// <summary>
/// A tiny live dashboard over a running test campaign: reads the per-group logs a campaign
/// writes (groupN.lst lists the implementations, groupN.txt is the tester's output so far,
/// groupN.done appears when the group ends) and serves a self-refreshing page.
/// Plain TcpListener, so it needs no URL ACL on Windows.
/// </summary>
static class Serve
{
    sealed record Failure(string Case, string Reason);

    /// <summary>Static facts about an implementation, shown and filtered on the page.</summary>
    sealed record Meta(int Total, string[] Kinds, string Verification, string Origin, string Approach, string Description);

    sealed class ImplStatus
    {
        public string Name { get; init; } = "";
        public string Group { get; init; } = "";
        public string State { get; set; } = "queued";
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int Total { get; init; }
        public string[] Kinds { get; init; } = [];
        public string Verification { get; init; } = "";
        public string Origin { get; init; } = "";
        public string Approach { get; init; } = "";
        public string Description { get; init; } = "";
        public string? Last { get; set; }
        public List<Failure> Failures { get; } = [];
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Run(Workspace ws, List<Implementation> known, string dir, string bind, int port)
    {
        var cases = Cases.Load(ws.Cases);
        var categories = LoadCategories(Path.Combine(ws.Repo, "categories.json"));
        var meta = known.ToDictionary(i => i.Name, i => Describe(i, cases.Count(c => c.AppliesTo(i.Name)), categories));
        var listener = new TcpListener(IPAddress.Parse(bind), port);
        listener.Start();
        Console.WriteLine($"serving {dir} on http://{bind}:{port}/  (Ctrl+C to stop)");
        while (true)
        {
            var client = listener.AcceptTcpClient();
            _ = Task.Run(() => Handle(client, dir, meta));
        }
    }

    static void Handle(TcpClient client, string dir, Dictionary<string, Meta> meta)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII);
            var requestLine = reader.ReadLine() ?? "";
            while (!string.IsNullOrEmpty(reader.ReadLine())) { }
            var path = requestLine.Split(' ').ElementAtOrDefault(1) ?? "/";

            var (type, body) = path.StartsWith("/status")
                ? ("application/json", JsonSerializer.Serialize(Status(dir, meta), JsonOptions))
                : ("text/html; charset=utf-8", Page);
            var bytes = Encoding.UTF8.GetBytes(body);
            var header = $"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            stream.Write(Encoding.ASCII.GetBytes(header));
            stream.Write(bytes);
        }
        catch (Exception e) when (e is IOException or SocketException)
        {
            // Client went away.
        }
    }

    static object Status(string dir, Dictionary<string, Meta> meta)
    {
        var impls = new List<ImplStatus>();
        foreach (var lst in Directory.EnumerateFiles(dir, "*.lst").Order(StringComparer.Ordinal))
        {
            var group = Path.GetFileNameWithoutExtension(lst);
            var log = Path.ChangeExtension(lst, ".txt");
            var groupDone = File.Exists(Path.ChangeExtension(lst, ".done"));
            var byName = new Dictionary<string, ImplStatus>();
            foreach (var name in File.ReadAllLines(lst).Where(l => l.Length > 0))
            {
                var s = Create(name, group, meta);
                byName[name] = s;
                impls.Add(s);
            }
            Parse(log, byName, groupDone);
        }

        // Single-implementation side runs (e.g. ansible.txt / malbolge.txt) without a .lst.
        foreach (var log in Directory.EnumerateFiles(dir, "*.txt"))
        {
            var stem = Path.GetFileNameWithoutExtension(log);
            if (File.Exists(Path.ChangeExtension(log, ".lst")) || stem is "info" or "finished")
                continue;
            var name = "json2dir-" + stem;
            if (!meta.ContainsKey(name) || impls.Any(i => i.Name == name))
                continue;
            var s = Create(name, stem, meta);
            impls.Add(s);
            Parse(log, new() { [name] = s }, File.Exists(Path.ChangeExtension(log, ".done")));
        }

        var info = File.Exists(Path.Combine(dir, "info.txt")) ? File.ReadAllText(Path.Combine(dir, "info.txt")).Trim() : "";
        var finished = File.Exists(Path.Combine(dir, "finished.txt")) ? File.ReadAllText(Path.Combine(dir, "finished.txt")).Trim() : null;
        return new { info, finished, now = DateTime.Now.ToString("HH:mm:ss"), impls };
    }

    static ImplStatus Create(string name, string group, Dictionary<string, Meta> meta)
    {
        var m = meta.GetValueOrDefault(name) ?? new Meta(0, [], "", "", "", "");
        return new ImplStatus
        {
            Name = name, Group = group, Total = m.Total, Kinds = m.Kinds, Verification = m.Verification,
            Origin = m.Origin, Approach = m.Approach, Description = m.Description,
        };
    }

    /// <summary>
    /// categories.json: { dimension: { category: [short names] } }; short name = name without "json2dir-".
    /// An implementation may sit in several categories of one dimension (F# is both Functional and JVM / .NET).
    /// </summary>
    static Dictionary<string, Dictionary<string, string[]>> LoadCategories(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string[]>>();
        if (!File.Exists(path))
            return result;
        var doc = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string[]>>>(File.ReadAllText(path)) ?? [];
        foreach (var (dimension, groups) in doc)
            result[dimension] = groups.SelectMany(g => g.Value.Select(n => (n, g.Key)))
                .GroupBy(x => x.n).ToDictionary(x => x.Key, x => x.Select(y => y.Key).ToArray());
        return result;
    }

    static Meta Describe(Implementation impl, int total, Dictionary<string, Dictionary<string, string[]>> categories)
    {
        var shortName = impl.Name.StartsWith("json2dir-") ? impl.Name["json2dir-".Length..] : impl.Name;
        string[] Category(string dimension, string fallback) =>
            categories.GetValueOrDefault(dimension)?.GetValueOrDefault(shortName) ?? [fallback];
        var d = impl.Description;
        var launcher = d.Contains("launcher", StringComparison.OrdinalIgnoreCase) || d.Contains("shell script") || d.Contains("shell program");
        return new Meta(
            total,
            Category("kind", "Other"),
            Category("verification", "None")[0],
            impl.Repo.Contains("/json2dir-guru/") ? "json2dir-guru" : "Third-party",
            launcher ? "Launcher (emits shell)" : "Native",
            d);
    }

    static void Parse(string log, Dictionary<string, ImplStatus> byName, bool groupDone)
    {
        if (!File.Exists(log))
            return;
        string[] lines;
        using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs, Encoding.UTF8))
            lines = sr.ReadToEnd().Split('\n');

        ImplStatus? current = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.StartsWith("== "))
            {
                if (current is not null)
                    current.State = "done";
                var name = line[3..].Split(' ')[0];
                current = byName.GetValueOrDefault(name);
                if (current is not null)
                    current.State = "running";
                continue;
            }
            if (current is null || line.Length < 2)
                continue;
            var caseName = line.Length > 2 ? line[2..].Split(' ')[0] : "";
            if (line.StartsWith("✓ "))
            {
                current.Passed++;
                current.Last = caseName;
            }
            else if (line.StartsWith("✗ "))
            {
                current.Failed++;
                current.Last = caseName;
                var reason = i + 1 < lines.Length ? lines[i + 1].Trim() : "";
                current.Failures.Add(new Failure(caseName, reason));
            }
            else if (line.StartsWith("- "))
            {
                current.Skipped++;
                current.Last = caseName;
            }
        }
        if (current is not null && groupDone)
            current.State = "done";
    }

    const string Page = """
        <!doctype html>
        <html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>json2dir-tester live</title>
        <style>
        :root{--bg:#fff;--fg:#1d1d1f;--mut:#6e6e73;--line:#e5e5ea;--ok:#2e9e5b;--bad:#d64545;--run:#2f6fd6;--bar:#f0f0f3}
        @media (prefers-color-scheme:dark){:root{--bg:#121214;--fg:#ececf0;--mut:#9a9aa3;--line:#2a2a30;--ok:#45c27a;--bad:#ef6a6a;--run:#5f95f0;--bar:#22222a}}
        body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:14px/1.4 system-ui,sans-serif}
        h1{font-size:18px;margin:0 0 4px} .mut{color:var(--mut)} .sum{margin:8px 0 14px;display:flex;gap:16px;flex-wrap:wrap}
        table{width:100%;border-collapse:collapse} td,th{padding:6px 8px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top}
        th{font-weight:600;color:var(--mut);font-size:12px;cursor:pointer;user-select:none} th.on{color:var(--fg)} tr.row{cursor:pointer}
        .bar{height:8px;background:var(--bar);border-radius:4px;overflow:hidden;min-width:80px;display:flex}
        .bar i{display:block;height:100%} .p{background:var(--ok)} .f{background:var(--bad)}
        .st{font-size:12px;padding:1px 6px;border-radius:9px;border:1px solid var(--line)}
        .running{color:var(--run)} .done{color:var(--ok)} .queued{color:var(--mut)} .num{font-variant-numeric:tabular-nums;white-space:nowrap}
        .fails{font-size:12px;color:var(--mut);white-space:pre-wrap;word-break:break-word} .bad{color:var(--bad)}
        .flt{display:flex;gap:10px;flex-wrap:wrap;margin:10px 0 0} .flt label{display:flex;flex-direction:column;font-size:12px;color:var(--mut);gap:2px}
        .flt select{font:inherit;font-size:13px;padding:4px 6px;border:1px solid var(--line);border-radius:6px;background:var(--bg);color:var(--fg);max-width:220px}
        .tag{font-size:11px;color:var(--mut)}
        @media (max-width:640px){.hide{display:none}}
        </style></head><body>
        <h1>json2dir-tester — live</h1><div class="mut" id="meta"></div>
        <div class="flt" id="flt"></div>
        <div class="sum" id="sum"></div>
        <table><thead><tr><th data-k="name">Implementation</th><th data-k="state">State</th><th>Progress</th><th class="num" data-k="passed">✓</th><th class="num" data-k="failed">✗</th><th class="num" data-k="pct">✗ %</th><th class="hide">Last case</th></tr></thead><tbody id="tb"></tbody></table>
        <script>
        const open=new Set();
        const DIMS=[['kinds','Тип языка'],['verification','Верификация'],['origin','Чья'],['approach','Подход'],['result','Результат']];
        const result=x=>x.state!=='done'?'Ещё идёт':x.failed===0?'Всё прошло':x.failed===1?'Одно падение':x.failed<=20?'2–20 падений':'Больше 20';
        const val=(x,k)=>k==='result'?[result(x)]:[].concat(x[k]);
        let filt={};try{filt=JSON.parse(localStorage.getItem('filt')||'{}')}catch(e){}
        let last=null,sig='';
        function buildFilters(impls){
          const box=document.getElementById('flt');
          box.innerHTML=DIMS.map(([k,label])=>{
            const vals=[...new Set(impls.flatMap(x=>val(x,k)))].sort();
            return `<label>${label}<select data-f="${k}"><option value="">все</option>${vals.map(v=>`<option${filt[k]===v?' selected':''}>${esc(v)}</option>`).join('')}</select></label>`;
          }).join('');
          box.querySelectorAll('select').forEach(s=>s.onchange=()=>{filt[s.dataset.f]=s.value;try{localStorage.setItem('filt',JSON.stringify(filt))}catch(e){}render()});
        }
        const pass=x=>DIMS.every(([k])=>!filt[k]||val(x,k).includes(filt[k]));
        let sortKey='state';try{sortKey=localStorage.getItem('sort')||'state'}catch(e){}
        const pct=x=>x.passed+x.failed?100*x.failed/(x.passed+x.failed):0;
        const order={running:0,queued:1,done:2};
        const cmp={name:(a,b)=>a.name.localeCompare(b.name),state:(a,b)=>order[a.state]-order[b.state]||a.name.localeCompare(b.name),
          passed:(a,b)=>b.passed-a.passed,failed:(a,b)=>b.failed-a.failed||pct(b)-pct(a),pct:(a,b)=>pct(b)-pct(a)||b.failed-a.failed};
        function esc(s){return String(s??'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]))}
        async function tick(){
          try{
            last=await (await fetch('/status',{cache:'no-store'})).json();
            const s=JSON.stringify(DIMS.map(([k])=>[...new Set(last.impls.flatMap(x=>val(x,k)))].sort()));
            if(s!==sig){sig=s;buildFilters(last.impls)}
            render();
          }catch(e){}
        }
        function render(){
          if(!last)return;
          {
            const d={...last,impls:last.impls.filter(pass)};
            d.impls.sort(cmp[sortKey]||cmp.state);
            document.querySelectorAll('th[data-k]').forEach(t=>t.classList.toggle('on',t.dataset.k===sortKey));
            let P=0,F=0,T=0,done=0,run=0;
            let h='';
            for(const x of d.impls){
              P+=x.passed;F+=x.failed;T+=x.total;if(x.state==='done')done++;if(x.state==='running')run++;
              const t=Math.max(x.total,1),pp=100*x.passed/t,fp=100*x.failed/t;
              h+=`<tr class="row" onclick="tog('${x.name}')"><td title="${esc(x.description)}">${esc(x.name.replace('json2dir-',''))}<div class="tag">${esc(x.kinds.join(' · '))}${x.verification!=='None'?' · '+esc(x.verification):''}</div></td>
              <td><span class="st ${x.state}">${x.state}</span></td>
              <td><div class="bar"><i class="p" style="width:${pp}%"></i><i class="f" style="width:${fp}%"></i></div>
              <span class="mut num">${x.passed+x.failed+x.skipped}/${x.total}</span></td>
              <td class="num">${x.passed}</td><td class="num ${x.failed?'bad':''}">${x.failed}</td><td class="num ${x.failed?'bad':''}">${pct(x).toFixed(1)}</td>
              <td class="hide mut">${esc(x.last??'')}</td></tr>`;
              if(open.has(x.name)&&x.failures.length)
                h+=`<tr><td colspan="7" class="fails">${x.failures.map(f=>'✗ '+esc(f.case)+'\n   '+esc(f.reason)).join('\n')}</td></tr>`;
            }
            document.getElementById('tb').innerHTML=h;
            document.getElementById('meta').textContent=`${d.info}  ·  обновлено ${d.now}`+(d.finished?`  ·  завершено ${d.finished}`:'');
            document.getElementById('sum').innerHTML=`<span>реализаций: <b>${d.impls.length}</b>${d.impls.length!==last.impls.length?' из '+last.impls.length:''}</span><span class="done">готово: <b>${done}</b></span><span class="running">идёт: <b>${run}</b></span><span>✓ <b>${P}</b></span><span class="bad">✗ <b>${F}</b></span><span class="mut">кейсов всего: ${T}</span>`;
          }
        }
        document.querySelectorAll('th[data-k]').forEach(t=>t.onclick=()=>{sortKey=t.dataset.k;try{localStorage.setItem('sort',sortKey)}catch(e){}tick()});
        function tog(n){open.has(n)?open.delete(n):open.add(n);tick()}
        tick();setInterval(tick,3000);
        </script></body></html>
        """;
}
