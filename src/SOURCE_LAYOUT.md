## Source layout

The `src` directory separates concrete projects from broader workspace scopes.

- `prj/` contains individual projects. A project is a concrete development or build unit and may represent a library, application, test project, tool, generator, or any other independently identifiable project.
- `wrk/` contains workspace scopes. A workspace represents a logical context that may span multiple related projects. It may contain items that belong to that context as a whole rather than to one specific project.

For example:

```text
src/
├─ prj/
│  ├─ Product.Core/
│  ├─ Product.Core.Tests/
│  ├─ Product.Hosting/
│  ├─ Product.Hosting.Tests/
│  ├─ Tools.Generator/
│  └─ Tools.Generator.Tests/
│
└─ wrk/
   ├─ Product/
   └─ Tools/
```

`Product.Core`, `Product.Core.Tests`, `Product.Hosting`, and `Product.Hosting.Tests` are separate projects and therefore belong under `prj/`.

`Product` is a workspace scope that can represent the broader context shared by those related projects without itself having to be a buildable project.

A workspace may contain shared inputs, schemas, fixtures, generated artifacts, orchestration, development resources, or other items whose ownership belongs to the workspace rather than to an individual project.

Projects and workspaces are therefore independent concepts:

- `prj/` answers **what is a concrete project?**
- `wrk/` answers **what belongs to a broader logical workspace?**

A project does not have to belong to a workspace, and a workspace does not have to contain buildable source code. An empty `wrk/` directory is valid when no workspace-level items are currently required.

Because Git does not track empty directories, `.gitkeep` may be used as a placeholder to retain an intentionally empty structural directory such as `wrk/`. The file has no semantic meaning beyond preserving that directory in the repository layout.
