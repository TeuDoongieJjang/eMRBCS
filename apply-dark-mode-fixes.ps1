# apply-dark-mode-fixes.ps1
# Run from the project folder (where eMRBCS.csproj is):   .\apply-dark-mode-fixes.ps1
# Makes a .bak copy of every file it changes. Reading.xaml is skipped (already rewritten).

$utf8 = New-Object System.Text.UTF8Encoding($true)

Get-ChildItem .\Pages\*.xaml | Where-Object { $_.Name -ne 'Reading.xaml' } | ForEach-Object {
    $path = $_.FullName
    $orig = [System.IO.File]::ReadAllText($path)
    $t = $orig

    # 1) hard-coded black text  ->  removed, so the themed implicit Label style applies
    $t = [regex]::Replace($t, '\s+TextColor="Black"', '')

    # 2) menu tile "upper" rectangles that were Fill="white"
    $t = [regex]::Replace($t, 'Fill="white"', 'Style="{StaticResource CardUpper}"', 'IgnoreCase')

    # 3) white cards / popups
    $t = $t.Replace('BackgroundColor="White"', 'BackgroundColor="{AppThemeBinding Light=White, Dark=#1E1E1E}"')
    $t = $t.Replace('<BoxView Color="White"',  '<BoxView Color="{AppThemeBinding Light=White, Dark=#1E1E1E}"')

    # 4) "Loading page / Performing task" overlay: its text is white, but the box behind it was transparent
    #    (invisible on a light page). Give that box a dark translucent background.
    $t = [regex]::Replace($t, '<BoxView Color="Transparent"(?=[^>]*Grid\.RowSpan="3")',
                          '<BoxView Color="#CC000000" CornerRadius="10"')

    # 5) plain string lists: give them a themed item template (default cell text is black on Android)
    $t = [regex]::Replace($t, '<ListView(\s+)(?!ItemTemplate)(?=x:Name=)',
                          '<ListView ItemTemplate="{StaticResource StringItemTemplate}"$1')

    if ($t -ne $orig) {
        Copy-Item $path "$path.bak" -Force
        [System.IO.File]::WriteAllText($path, $t, $utf8)
        Write-Host "updated  $($_.Name)"
    }
}
Write-Host "Done. Rebuild, then test the app with the phone in Light AND Dark mode."