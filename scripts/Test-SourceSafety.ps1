#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $files = @(git -c core.quotepath=false ls-files --cached)
    if ($LASTEXITCODE -ne 0 -or !$files.Count) { throw 'No indexed source files to verify.' }
    $rootFiles = @('.gitattributes','.gitignore','Branding.props','Directory.Build.props','LICENSE','README.md','THIRD-PARTY-NOTICES.md','dependency-manifest.json','构建.ps1')
    $scripts = @('本地多轮生成测试.ps1','创建根目录EXE.ps1','核验多轮结果.ps1','生成应用图标.ps1','双构建并冻结.ps1','Import-Dependencies.ps1','Test-Dependencies.ps1','Test-SourceSafety.ps1')
    $problems = [Collections.Generic.List[object]]::new()
    $patterns = @(
        '-----BEGIN (?:OPENSSH |RSA |EC |DSA )?PRIVATE KEY-----',
        ('gh'+'[pousr]_[A-Za-z0-9]{24,}'),
        ('github'+'_pat_[A-Za-z0-9_]{30,}'),
        '(?i)(?:password|api[_-]?key|access[_-]?token)\s*[:=]\s*["''][A-Za-z0-9_+/-]{16,}["'']',
        '(?i)[A-Z]:[\\/](?:Users[\\/][^\\/\s"'']+|59934|流光核心源码)[\\/]'
    )
    foreach ($relative in $files) {
        $allowed = $relative -in $rootFiles -or $relative -match '^(src|assets|docs|\.github)/'
        if ($relative.StartsWith('scripts/')) { $allowed = $relative.Substring(8) -in $scripts }
        if (!$allowed -or $relative -match '(?i)(^|/)(bin|obj|history|evidence|integrations)/|\.(dll|exe|pdb|zip|7z|wld|db|sqlite|log|pem|key|pfx|p12)(\.|$)|(^|/)\.env') {
            $problems.Add([pscustomobject]@{ Path=$relative; Reason='Forbidden publication path' }); continue
        }
        if ($relative.EndsWith('.png') -or $relative.EndsWith('.ico')) {
            if ($relative -notmatch '^assets/branding/(icon-source\.png|MapStudio\.png|MapStudio\.ico)$') {
                $problems.Add([pscustomobject]@{ Path=$relative; Reason='Unexpected binary asset' })
            }
            continue
        }
        foreach ($hit in Select-String -LiteralPath (Join-Path $root $relative) -Pattern $patterns) {
            # Never echo matched values: an accidental secret must not enter logs.
            $problems.Add([pscustomobject]@{ Path=$relative; Line=$hit.LineNumber; Reason='Secret or personal local path pattern' })
        }
    }
    [pscustomobject]@{ IndexedFiles=$files.Count; Problems=$problems.Count; Findings=@($problems.ToArray()); Scope='Filename allowlist + targeted text patterns; not an exhaustive security audit' }
    if ($problems.Count) { throw 'Publication boundary check failed.' }
} finally { Pop-Location }
