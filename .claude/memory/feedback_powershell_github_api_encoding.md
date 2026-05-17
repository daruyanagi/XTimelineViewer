---
name: PowerShell で GitHub API に日本語を送る際のエンコード
description: Invoke-RestMethod は日本語テキストを文字化けさせる。WebClient + UTF-8 バイト列を使うこと。
type: feedback
---

PowerShell から GitHub API に日本語を含む JSON を送るときは `Invoke-RestMethod` / `ConvertTo-Json` の組み合わせを使わない。

**Why:** `Invoke-RestMethod` は内部エンコードが UTF-8 でない場合があり、日本語が文字化けしてリリースノートなどが壊れた実績がある。

**How to apply:** 代わりに `System.Net.WebClient.UploadData()` で UTF-8 バイト列として送る。

```powershell
$body = @{ body = $notes } | ConvertTo-Json
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)

$client = New-Object System.Net.WebClient
$client.Headers.Add('Authorization', "token $token")
$client.Headers.Add('Accept', 'application/vnd.github.v3+json')
$client.Headers.Add('Content-Type', 'application/json; charset=utf-8')
$client.Headers.Add('User-Agent', 'PowerShell')

$responseBytes = $client.UploadData($url, 'PATCH', $bodyBytes)
$result = [System.Text.Encoding]::UTF8.GetString($responseBytes) | ConvertFrom-Json
```
