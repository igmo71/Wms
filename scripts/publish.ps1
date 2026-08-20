param(
    [string]$Registry = "192.168.1.55:5000",
    [string]$Tag = (Get-Date -Format "yyyy-MM-dd_HH-mm")
)

$ErrorActionPreference = "Stop"

dotnet publish .\Wms.WebApi\Wms.WebApi.csproj `
  -c Release `
  --os linux `
  --arch x64 `
  /t:PublishContainer `
  -p:ContainerRepository=wms-webapi `
  -p:ContainerImageTag=$Tag

docker tag wms-webapi:$Tag $Registry/wms-webapi:$Tag
docker push $Registry/wms-webapi:$Tag

dotnet publish .\Wms.WebApp\Wms.WebApp.csproj `
  -c Release `
  --os linux `
  --arch x64 `
  /t:PublishContainer `
  -p:ContainerRepository=wms-webapp `
  -p:ContainerImageTag=$Tag

docker tag wms-webapp:$Tag $Registry/wms-webapp:$Tag
docker push $Registry/wms-webapp:$Tag

Write-Host ""
Write-Host "Published:"
Write-Host "$Registry/wms-webapi:$Tag"
Write-Host "$Registry/wms-webapp:$Tag"
Write-Host ""
Write-Host "Set WMS_TAG=$Tag in .env"