param(
    [string]$Registry = "192.168.1.55:5000",
    [string]$Tag = (Get-Date -Format "yyyy-MM-dd_HH-mm")
)

$ErrorActionPreference = "Stop"

function Assert-ExitCode([int]$ExitCode, [string]$Step) {
    if ($ExitCode -ne 0) {
        throw "$Step failed with exit code $ExitCode."
    }
}

dotnet publish .\Wms.WebApi\Wms.WebApi.csproj `
  -c Release `
  --os linux `
  --arch x64 `
  /t:PublishContainer `
  -p:ContainerRepository=wms-webapi `
  -p:ContainerImageTag=$Tag `
  -p:LocalRegistry=Docker
Assert-ExitCode $LASTEXITCODE "WebApi container publish"

dotnet publish .\Wms.WebApp\Wms.WebApp.csproj `
  -c Release `
  --os linux `
  --arch x64 `
  /t:PublishContainer `
  -p:ContainerRepository=wms-webapp `
  -p:ContainerImageTag=$Tag `
  -p:LocalRegistry=Docker
Assert-ExitCode $LASTEXITCODE "WebApp container publish"

docker tag wms-webapi:$Tag $Registry/wms-webapi:$Tag
Assert-ExitCode $LASTEXITCODE "WebApi image tag"
docker tag wms-webapp:$Tag $Registry/wms-webapp:$Tag
Assert-ExitCode $LASTEXITCODE "WebApp image tag"

docker push $Registry/wms-webapi:$Tag
Assert-ExitCode $LASTEXITCODE "WebApi image push"
docker push $Registry/wms-webapp:$Tag
Assert-ExitCode $LASTEXITCODE "WebApp image push"

Write-Host ""
Write-Host "Published:"
Write-Host "$Registry/wms-webapi:$Tag"
Write-Host "$Registry/wms-webapp:$Tag"
Write-Host ""
Write-Host "Set WMS_TAG=$Tag in the target environment file"
