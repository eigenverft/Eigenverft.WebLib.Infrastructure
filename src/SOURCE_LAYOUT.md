## Source layout

The `src` directory separates concrete projects from solution scopes.

- `prj/` contains individual buildable projects.
- `sln/` contains one directory per solution. Each directory owns one `.slnx` file and may contain solution-level development files such as a readme and a `.gitignore` for Visual Studio state.

For example:

```text
src/
├─ prj/
│  ├─ Product.Core/
│  ├─ Product.Core.Tests/
│  ├─ Product.Hosting/
│  └─ Product.Hosting.Tests/
│
└─ sln/
   ├─ Product.Core/
   │  └─ Product.Core.slnx
   └─ Product.Hosting/
      └─ Product.Hosting.slnx
```

A solution directory groups the projects needed for one development, build, test, pack, or publish scope. Keeping every `.slnx` in its own directory also allows `dotnet` commands to find that solution without a path argument.
