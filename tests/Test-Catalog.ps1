$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'Release-UploadWatchdog.ps1'),[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Worker syntax invalid' }
foreach ($name in @('L','Get-PagedGitHubItems','Get-CatalogChoices')) {
    $node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name}.GetNewClosure(),$true)
    . ([scriptblock]::Create($node.Extent.Text))
}
$Language='en'
$script:requests=New-Object 'System.Collections.Generic.List[string]'
function Invoke-GitHubGet([string]$uri) {
    $script:requests.Add($uri)
    if ($uri -match '/user/repos') {
        if ($uri -match 'page=1$') {
            return ,@(1..100 | ForEach-Object { @{full_name="owner/repo$_";permissions=@{push=($_ -eq 1)};archived=$false} })
        }
        return ,@(@{full_name='owner/archived';permissions=@{push=$true};archived=$true},@{full_name='owner/second';permissions=@{push=$true};archived=$false})
    }
    return ,@(@{tag_name='published';name='Public';draft=$false},@{tag_name='v-draft';name='Draft';draft=$true})
}
$projects=@(Get-CatalogChoices '')
if ($projects.Count -ne 2 -or $projects[0].value -ne 'owner/repo1' -or $projects[1].value -ne 'owner/second') { throw 'Writable/non-archived catalog filtering failed' }
if ($script:requests.Count -ne 2 -or $script:requests[1] -notmatch '&per_page=100&page=2$') { throw 'Repository pagination failed' }
$drafts=@(Get-CatalogChoices 'owner/repo1')
if ($drafts.Count -ne 1 -or $drafts[0].value -ne 'v-draft' -or $drafts[0].label -notmatch 'v-draft') { throw 'Only existing drafts must be selectable' }
$count=$script:requests.Count
$rejected=$false
try { Get-CatalogChoices 'https://wrong.test/repo' | Out-Null } catch { $rejected=$true }
if (-not $rejected -or $script:requests.Count -ne $count) { throw 'Invalid repository reached API' }
function Invoke-GitHubGet { return @() }
if (@(Get-CatalogChoices '').Count -ne 0 -or @(Get-CatalogChoices 'owner/repo').Count -ne 0) { throw 'Empty catalog was not handled' }
Write-Host 'Catalog checks passed: read-only mocks, boxed PS5 pagination, write access, archives, drafts, invalid repository and empty lists.'
