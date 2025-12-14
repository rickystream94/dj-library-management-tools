param
(
    [Parameter(Mandatory=$true)]
    [ValidateSet("Major,Minor,Build")]
    [string] $VersionSegmentToIncrement
)

# Check that we're on master branch
if ((git rev-parse --abbrev-ref HEAD) -ne "master")
{
    Write-Error "Error: You must be on the 'master' branch to push a git tag."
    return
}

$csprojFilePath = "..\src\LibTools4DJs\LibTools4DJs.csproj"
[xml]$proj = Get-Content $csprojFilePath
$currentVersion = [System.Version]::Parse($proj.Project.PropertyGroup[0].Version)

Write-Host "Detected current version: v$currentVersion"
Write-Host "Incrementing $VersionSegmentToIncrement version segment"
$newVersion = switch ($VersionSegmentToIncrement) {
    "Major" { [System.Version]::new($currentVersion.Major + 1, 0, 0) }
    "Minor" { [System.Version]::new($currentVersion.Major, $currentVersion.Minor + 1, 0) }
    "Build" { [System.Version]::new($currentVersion.Major, $currentVersion.Minor, $currentVersion.Build + 1) }
    default { throw "Invalid version segment: $VersionSegmentToIncrement" }
}

Write-Host "New version: v$newVersion"
Write-Host "Updating version in .csproj file" -ForegroundColor Cyan
$proj.Project.PropertyGroup[0].Version = $newVersion.ToString()
$proj.Save($csprojFilePath)

Write-Host "Committing version bump and pushing to remote" -ForegroundColor Cyan
git add $csprojFilePath
git commit -m "Bumped project version to v$newVersion"
git push

Write-Host "Pushing new git tag v$newVersion" -ForegroundColor Cyan
git tag "v$newVersion"
git push origin "v$newVersion"

Write-Host "Version tag v$newVersion pushed successfully." -ForegroundColor Green