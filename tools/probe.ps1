param([string]$Filter='vid_3554')
Add-Type -Path "$PSScriptRoot\HidProbe.cs"
foreach($p in [HidProbe]::Paths($Filter)){ "{0}`n    {1}" -f $p, [HidProbe]::Caps($p) }
