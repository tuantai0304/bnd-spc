$ErrorActionPreference = "Continue"
$base = "http://localhost:5090"

function Post($body) {
    try {
        $r = Invoke-WebRequest "$base/api/travel-requests" -Method Post -ContentType "application/json" -Body $body -UseBasicParsing -TimeoutSec 10
        return @{ code = [int]$r.StatusCode; body = $r.Content }
    } catch {
        $resp = $_.Exception.Response
        if ($resp) {
            $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
            return @{ code = [int]$resp.StatusCode; body = $sr.ReadToEnd() }
        }
        return @{ code = -1; body = $_.Exception.Message }
    }
}

function Get($path) {
    try {
        $r = Invoke-WebRequest "$base$path" -UseBasicParsing -TimeoutSec 10
        return @{ code = [int]$r.StatusCode; body = $r.Content }
    } catch {
        $resp = $_.Exception.Response
        if ($resp) {
            $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
            return @{ code = [int]$resp.StatusCode; body = $sr.ReadToEnd() }
        }
        return @{ code = -1; body = $_.Exception.Message }
    }
}

Write-Output "=== 1. GET /api/planets ==="
$p = Get "/api/planets"; "$($p.code)  $($p.body)"

Write-Output ""
Write-Output "=== 2. Valid call: Angel 1 (1) -> Argus X (5) ==="
$r = Post '{"originPlanetId":1,"destinationPlanetId":5,"lifeForms":[{"species":"Vulcan","weightKg":68}]}'
"$($r.code)  $($r.body)"

Write-Output ""
Write-Output "=== 3. Same-planet call -> expect 400 ==="
$r = Post '{"originPlanetId":1,"destinationPlanetId":1,"lifeForms":[{"species":"Vulcan","weightKg":68}]}'
"$($r.code)  $($r.body)"

Write-Output ""
Write-Output "=== 4. Unknown planet 99 -> expect 400 ==="
$r = Post '{"originPlanetId":1,"destinationPlanetId":99,"lifeForms":[{"species":"Vulcan","weightKg":68}]}'
"$($r.code)  $($r.body)"

Write-Output ""
Write-Output "=== 5. Zero life forms -> expect 400 ==="
$r = Post '{"originPlanetId":1,"destinationPlanetId":2,"lifeForms":[]}'
"$($r.code)  $($r.body)"

Write-Output ""
Write-Output "=== 6. Negative weight -> expect 400 ==="
$r = Post '{"originPlanetId":1,"destinationPlanetId":2,"lifeForms":[{"species":"Gorn","weightKg":-5}]}'
"$($r.code)  $($r.body)"

Write-Output ""
Write-Output "=== 7. Party of 21 -> expect 201 Rejected ==="
$forms = (1..21 | ForEach-Object { '{"species":"Tribble","weightKg":2}' }) -join ","
$r = Post "{`"originPlanetId`":1,`"destinationPlanetId`":3,`"lifeForms`":[$forms]}"
"$($r.code)  $($r.body)"

Write-Output ""
Write-Output "=== 8. Single 5000kg life form -> expect 201 Rejected ==="
$r = Post '{"originPlanetId":1,"destinationPlanetId":4,"lifeForms":[{"species":"Horta","weightKg":5000}]}'
"$($r.code)  $($r.body)"

Write-Output ""
Write-Output "=== 9. GET /api/shuttles ==="
$s = Get "/api/shuttles"; "$($s.code)  $($s.body)"

Write-Output ""
Write-Output "=== 10. Waiting 16s for the Argus X trip (4 rank steps x 3s) to complete ==="
Start-Sleep -Seconds 16

Write-Output "=== 11. GET /api/travel-requests/1 ==="
$t = Get "/api/travel-requests/1"; "$($t.code)  $($t.body)"

Write-Output ""
Write-Output "=== 12. GET /api/travel-history/stats ==="
$st = Get "/api/travel-history/stats"; "$($st.code)  $($st.body)"

Write-Output ""
Write-Output "=== 13. GET /api/travel-history ==="
$h = Get "/api/travel-history?page=1&pageSize=10"; "$($h.code)  $($h.body)"

Write-Output ""
Write-Output "=== 14. Bad page size -> expect 400 ==="
$b = Get "/api/travel-history?page=1&pageSize=5000"; "$($b.code)  $($b.body)"

Write-Output ""
Write-Output "=== 15. Unknown request id -> expect 404 ==="
$n = Get "/api/travel-requests/9999"; "$($n.code)  $($n.body)"
