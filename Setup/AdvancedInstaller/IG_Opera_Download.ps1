
$url = "https://net.geo.opera.com/opera/stable/windows?utm_source=IMAGEGLASS&utm_medium=pb&utm_campaign=imagebundle"
$tempPath = [System.IO.Path]::GetTempPath()
$outpath = "$tempPath/IG_OperaSetup.exe"
Invoke-WebRequest -Uri $url -OutFile $outpath

$args = @("--silent", "--allusers=0", "--show-onboarding-overlay", "--setdefaultbrowser=1")
Start-Process -Filepath $outpath -ArgumentList $args
