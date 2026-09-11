# Contributing to WALL-EVE

Thank you for your interest in contributing to WALL-EVE! 🚀

## 🤝 How to Contribute

### Reporting Bugs

If you find a bug, please open an issue with:
- **Clear description** of the problem
- **Steps to reproduce** the issue
- **Expected vs. actual behavior**
- **Screenshots** if applicable
- **Environment info** (OS, .NET version)

### Suggesting Features

Feature requests are welcome! Please open an issue with:
- **Description** of the feature
- **Use case** - why would this be useful?
- **Mockups/Examples** if applicable

### Pull Requests

1. **Fork** the repository
2. **Create a branch** for your feature (`git checkout -b feature/amazing-feature`)
3. **Make your changes**
4. **Test thoroughly** - make sure nothing breaks
5. **Commit** with clear messages (`git commit -m 'Add amazing feature'`)
6. **Push** to your fork (`git push origin feature/amazing-feature`)
7. **Open a Pull Request** against `dev` (not `master` or `main`).

For tracked work, follow the [single-issue execution prompt](docs/ISSUE-EXECUTION.md). Use one branch from current `origin/dev` and one PR per executable issue. Tracking issues group child issues; do not implement them as one PR. Dependencies must already be integrated into `dev`. Leave every PR open for external review and explicit maintainer approval; never merge, enable auto-merge, or close its issue autonomously.

## 📋 Development Setup

1. Clone the repo:
   ```bash
   git clone https://github.com/YOUR_USERNAME/WALL-Eve.git
   cd WALL-Eve
   ```

2. Install dependencies:
   ```bash
   dotnet restore
   ```

3. Create EVE Developer Application:
   - Go to https://developers.eveonline.com/
   - Create an app with callback URL: `http://localhost:5000/callback`
   - Copy Client ID to `appsettings.json`

4. Run the app:
   ```bash
   dotnet run
   ```

## 🎨 Code Style

- Follow **C# Coding Conventions**: https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
- Use **meaningful variable names**
- **Comment complex logic** - help future developers understand your code
- Keep methods **focused and short** (ideally < 50 lines)
- Use **async/await** for I/O operations

## 📝 Commit Message Guidelines

- Use **present tense** ("Add feature" not "Added feature")
- Use **imperative mood** ("Move cursor to..." not "Moves cursor to...")
- **Limit first line to 72 characters**
- Reference issues/PRs where applicable

Examples:
```
Add constellation-based edge coloring to map
Fix authentication token refresh bug (#123)
Update README with new map features
```

## 🧪 Testing

Before submitting a PR:
- ✅ Build succeeds (`dotnet build`)
- ✅ No compiler warnings
- ✅ App starts without errors
- ✅ Manual testing of affected features
- ✅ No sensitive data (tokens, personal DBs) in commits

## 🗺️ Map Development

If you're working on map features, please read the extensive comments in:
- `Models/Map/SystemActivity.cs` - Live data flow
- `Models/Map/MapConnection.cs` - Graph structure
- `Components/Map/MapCanvas.razor` - Coordinate transformation
- `wwwroot/js/cytoscape-map.js` - Rendering & styling

## 📚 Resources

- **EVE ESI Docs**: https://esi.evetech.net/ui/
- **EVE Developer Docs**: https://developers.eveonline.com/docs/
- **Cytoscape.js Docs**: https://js.cytoscape.org/
- **Blazor Docs**: https://learn.microsoft.com/en-us/aspnet/core/blazor/

## ⚖️ License

By contributing, you agree that your contributions will be licensed under the MIT License.

## 🙏 Attribution

If you make significant contributions, feel free to add yourself to the Credits section in README.md!

## 💬 Questions?

Feel free to open an issue with the `question` label if you need help or clarification.

---

Thank you for making WALL-EVE better! 🎉
