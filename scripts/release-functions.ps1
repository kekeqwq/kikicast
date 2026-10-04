# .NET file hashing avoids architecture-sensitive Windows PowerShell module
# autoloading when the host shell is emulated on ARM64. No content is logged.
function Get-KikicastSHA256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $algorithm.Dispose(); $stream.Dispose() }
}
