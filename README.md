# CodeAssistant

Terminal chat assistant powered by GitHub Copilot. Windows x64, no .NET install required.

## Download and run

```powershell
iwr https://github.com/NephChevsky/CodeAssistant/releases/latest/download/CodeAssistant.TUI.exe -OutFile CodeAssistant.TUI.exe; .\CodeAssistant.TUI.exe
```

The app asks for a GitHub token on start. Type `exit` to quit.

## Releases

Push a tag such as `v1.0.0` to trigger the publish workflow, which attaches the exe to a GitHub release.
