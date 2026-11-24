# CodeAssistant

Terminal chat assistant powered by GitHub Copilot. Windows x64, no .NET install required.

## Download and run

```powershell
iwr https://github.com/NephChevsky/CodeAssistant/releases/latest/download/CodeAssistant.TUI.exe -OutFile CodeAssistant.TUI.exe; .\CodeAssistant.TUI.exe
```

The app asks for a GitHub token on start. Type `exit` to quit.

## Web app

`CodeAssistant.WebApp` is an ASP.NET Core app with a React front end (`ClientApp`), offering the chat modes and the feature planner. It only listens on `http://localhost:5080` and has no authentication. Requires Node.js.

```powershell
dotnet user-secrets set ConnectionStrings:Copilot <github-token> --project CodeAssistant.WebApp
dotnet run --project CodeAssistant.WebApp
```

In development, the Vite dev server is started automatically and the browser opens `http://localhost:5173`. `dotnet publish` builds the React app into `wwwroot`.

## Releases

Push a tag such as `v1.0.0` to trigger the publish workflow, which attaches the exe to a GitHub release.
