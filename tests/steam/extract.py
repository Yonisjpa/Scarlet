"""Saca de src/Steam/SteamCdp.cs los scripts que Scarlet corre dentro de Steam (scripts.json) para probarlos en sim.js."""
import re, json, pathlib
HERE = pathlib.Path(__file__).parent
__SRC__ = HERE.parent.parent / 'src' / 'Steam' / 'SteamCdp.cs'
__OUT__ = HERE / 'scripts.json'
src=open(__SRC__, encoding='utf-8').read()
def const(name):
    m=re.search(r'(?:const string|static string) '+name+r' (?:=|=>) @"(.*?)";\n', src, re.S)
    return m.group(1).replace('""','"')
DlNorm=const('DlNorm')
fix=lambda t: t.replace('" + DlNorm + @"', DlNorm)
Js=fix(const('Js')); WaitNext=const('WaitNext'); Dl=fix(const('DownloadsJs'))
vals={'Complete':'14','ShowConfig':'7','ShowEULAs':'8','Canceled':'16','Failed':'15','string.Join(",", NeedsSteam)':'3,4,6,10,11,12,13'}
def method(name):
    return re.search(r'Task<JsonElement> '+name+r'\(.*?return await EvalAsync\(\$@"(.*?)", \d+\);', src, re.S).group(1)
def interp(t, extra):
    v=dict(vals); v.update(extra); v['Js']=Js; v['WaitNext']=WaitNext
    out=''; i=0
    while i<len(t):
        if t.startswith('{{',i): out+='{'; i+=2; continue
        if t.startswith('}}',i): out+='}'; i+=2; continue
        if t.startswith('""',i): out+='"'; i+=2; continue
        if t[i]=='{':
            j=i+1; d=1
            while d:
                d += {'{':1,'}':-1}.get(t[j],0); j+=1
            e=t[i+1:j-1]
            if e not in v: raise SystemExit('falta '+e)
            out+=v[e]; i=j; continue
        out+=t[i]; i+=1
    return out
S={'prepare': interp(method('PrepareAsync'), {'appid':'__APPID__'}),
 'folder': interp(method('SetFolderAsync'), {'appid':'__APPID__','folder':'__FOLDER__'}),
 'go': interp(method('ContinueAsync'), {'appid':'__APPID__','folder':'__FOLDER__'}),
 'eulaYes': interp(method('EulaAsync'), {'appid':'__APPID__','(accept ? "true" : "false")':'true','marks':'SteamClient.Apps.MarkEulaAccepted(app, "e1", 3);','JsonSerializer.Serialize(eulas.Select(e => e.id))':'["e1"]'}),
 'eulaNo': interp(method('EulaAsync'), {'appid':'__APPID__','(accept ? "true" : "false")':'false','marks':'','JsonSerializer.Serialize(eulas.Select(e => e.id))':'[]'}),
 'downloads': Dl,
 'cancel': interp(re.search(r'public static async Task CancelAsync\(\)\s*\{\s*try \{ await EvalAsync\(\$@"(.*?)", 4000\)', src, re.S).group(1), {})}
def inmethod(name, extra):
    i=src.index(name)
    m=re.compile(r'EvalAsync\(\$@"(.*?)", \d+\)', re.S).search(src, i)
    return interp(m.group(1), extra)
S['pause']=inmethod('Task<bool> PauseAsync', {'appid':'__APPID__'})
S['resume']=inmethod('Task<bool> ResumeAsync', {'appid':'__APPID__'})
S['cancelDl']=inmethod('Task<string> CancelDownloadAsync', {'appid':'__APPID__','DlNorm':DlNorm,'(uninstall ? "true" : "false")':'__UNINST__'})
S['now']=inmethod('Task<bool> UpdateNowAsync', {'appid':'__APPID__'})
S['uninstall']=inmethod('Task<bool> UninstallAsync', {'appid':'__APPID__'})
S['cloud']=const('CloudJs')
json.dump(S, open(__OUT__,"w",encoding="utf-8"), indent=1, ensure_ascii=False)
print("scripts.json:", len(S), "scripts")
