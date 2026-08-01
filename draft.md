# the goal: 

a fully capable, state of the art mcp for vvvv gamma, which can read, create, edit (running) patches/plugins/shaders and can perfectly explain any patch.




# community api

since there is no official api for vvvv, we need to create a community api. this might become rough on the edges, but should still work, since vvvv patches (vl files) are not compiled and are simply xml files holding info about nodes, connections, etc. Custom user nodes are .csproj/cs files, shader are stride files (using stride's sdsl), vvvv console output could be sent to the mcp via a custom node we provide, same for snapshots of rendering (eg spout), etc.
so, while our api wont be able to eg open the nodebrowser of a running vvvv instance and type in a node name, i can see we could put together a sufficent api, which offers functions for creating/removing/editing nodes and graphs (by editing the xml), custom plugins, reading patches, explaining patches, checking the output of patches etc. 



# nodeset dictionary

thats probably the biggest issue, since there is no registry of nodes afaik, their pins or types thereof.....then there are advanced concepts like generically typed nodes, (im)mutable nodes, classes, records, operations, reactive regions, gpu compute systems, primitive regions like for loops, repeat, etc.
Some work has already been done in this repo in this regard, but i'm not sure if it is a solution yet. Another way to hack it which i can think of is doing some kind of analysis-bench, where a bot creates one node after the other, to check their ins and outs, then tries to connect them to a few nodes to check their type. Possible....We could re-run this with every new release.
This "mdictionary" of all nodes will be a living document, since nodes get changed, some things might have been analyzed incorrectly and need a fix, etc....


# documentation + helpfiles
there is the "gray book" online from vvvv which serves as main resource and starting point. other resources are the forum, a few matrix chat channels and the helpbrowser. The better plugins/nugets also ship their own helpfile, sometimes docs on their github, etc.
Since in gamma you can patch natively with a lot of existing .net libraries, also custom nodes are c#, classic .net knowledge can be applied in these cases too.
For the hlsl superset of stride called sdsl, there is a skill for agents (see below) which we can use to collect all necessary info, and besides the gray book there is also the official stride documentation.
many nodes themselves have been made in vvvv, we should always check and use these patches (from plugins or core) as learning resources.


# tests + feedback
ideally we can make use of the new long-shot capabilities of llms and get as much feedback from vvvv (console, node outputs, patch outputs, etc. - since we can easily create new vvvv nodes, we can create some sending nodes to capture this data) so the mcp can create complex, working patches without human interaction. we can also think about creating additional *_test.vl files, or define tests within the patches themselves- tbd. Running separate files would potentially also mean needing shell access, and/or mouse and keyboard access similar to how some mcps control browsers....


!IMPORTANT: everything here refers to vvvv gamma, the new branch, not beta, unless explicitly stated. vvvv gamma is also called VL, which is also the new file extension *.vl!

there are skills for sdsl by tebjan which we should use: https://github.com/tebjan/vvvv-skills



ideally we set this up as an evolving loop of independent agents, which we continously run to improve the mcp; eg one scrapes the forum/releases/changelogs/nuget packages/etc. once a month and writes new stuff into a db, another one analyses broken generated patches to learn from them and writes into the db, another agent is triggered by new db entries, filters them and if necessary, applies new knowledge to the mcp, updates the api, and so on - you get the idea....


we should also recursively scrape all help files and other patches for all nodes, then save per node where it is used so we/the mcp can look up examples


update bonus:
create a suite of challenges (=hard vvvv patches) and let the mcp do it to test different models using the mcp, with scoreboard/ranking on a webpage...


---

# phase 3: true remote control of a running vvvv instance

*research findings — not yet implemented*

## the question

can the mcp actually control a running vvvv? not just edit xml files on disk, but: know which patch is running, open/close patches, install packs, read live values, write parameters, trigger reloads — essentially use vvvv as a programmable runtime from the outside.

## what we found

### vvvv is a .net app — all doors are open from the inside

vvvv gamma is a regular .net 8 application. any c# code running inside it has full access to:
- the running patch state
- the document model (which files are open, their canvas/node/link structure)
- live pin values on any running node
- errors and warnings
- the service registry (`AppHost.Global.Services`)

crucially: **you can attach a vs/vs code debugger to a running vvvv process** just like any other .net app. this means any bridge code we write inside vvvv can be developed and debugged with full ide support — no blind ipc debugging.

### the `.HDE.vl` extension mechanism

any file named `*.HDE.vl` that vvvv loads **automatically runs inside the editor** as an extension. this is the official, supported way to extend the editor. it gives you:
- access to `VL.Lang` session nodes (live editor api)
- which documents are open
- ability to register menu commands + keyboard shortcuts
- ability to spawn custom ui panels

this is the cleanest entry point for a bridge that needs editor-level access (open/close patches, navigate, install packs).

### key internal apis

**`AppHost`** — the master key:
```csharp
AppHost.Global           // the editor's global host, accessible from any node
  .Services              // full DI service registry — get any registered service
  .SynchronizationContext// post work to the vvvv main loop from another thread
  .LoadPlugin(path)      // load a dll into vvvv at runtime
  .App                   // the running application object
  .NodeFactoryRegistry   // all registered node factories
```

**`VL.Lang.PublicAPI`** — live patch state:
```csharp
ILiveElement             // any running element → data stream, errors, messages
ILiveDataHub             // any pin/pad → value, IsConnected, CreateDataChannel()
                         // CreateDataChannel() gives R/W access to any pin value
ILiveNodeApplication     // a running node → all pins, timing, learn mode
ILiveLink                // a wire → source and sink data hubs
```

**`VL.Model`** — document model:
```csharp
VL.Model.Solution        // all open documents
VL.Model.VLSession       // the session
  .CurrentSolution       // access the current solution
VL.Model.Canvas          // a patch canvas
VL.Model.DataHub         // a pin/pad/control point
```

**`IStartup` interface** — participate in vvvv startup:
```csharp
void Configure(AppHost)  // called during startup, register your services here
```

### what does NOT exist

- no `IHDEHost` scripting interface (that was vvvv4/beta, removed in gamma)
- no built-in http/websocket server on any port by default
- no external cli to send commands to a running vvvv
- no repl or named pipe server waiting by default

however: **aspnet core / kestrel is already loaded in the vvvv process**. a c# node can start a full http server with zero extra dependencies.

### existing io packs that help

| pack | what it gives us |
|---|---|
| `VL.IO.WebSocket` | websocket server/client nodes, has "web ui to control an app" help patches |
| `VL.IO.OSCQuery` | zero-config http+websocket server exposing all public channels (http schema, ws updates) |
| `VL.IO.Pipes` | named pipe ipc — "howto: inter-process communication via namedpipes" |
| `VL.IO.OSC` | osc udp — bidirectional, works with max/pd/touchdesigner etc |
| `VL.IO.Redis` | bind channels to redis — useful if mcp runs as a service |

### the oscquery shortcut

adding a single `OSCQueryServer` node to a patch gives instant:
- `GET /` → json schema of all public channels  
- websocket updates when any channel changes
- `PUT /<channel>` → write a value

this covers "read/write running parameters" with zero custom code.

## recommended architecture for when we build this

```
mcp server (this repo)
    │
    ├── xml editing      ← always available, creates/modifies .vl files on disk
    │                       vvvv hot-reloads on file save automatically
    │
    ├── VL.MCP.HDE.vl   ← editor extension (auto-loads), opt-in for users
    │   (websocket or    ← exposes:
    │    http on fixed      · which documents are open (solves "which file to edit")
    │    port e.g. 7123)    · compilation errors + warnings (feedback loop)
    │                       · trigger explicit reload after xml edit
    │                       · open/close/navigate patches
    │                       · list installed packs
    │
    └── VL.MCPBridge    ← optional process node dropped into a running patch
        (node in patch)    · live pin value read/write via ILiveDataHub
                           · console output capture
                           · rendering snapshots (spout / screengrab)
                           · public channel read/write
```

the mcp degrades gracefully:
- no bridge running → xml edit + explain only (works today)
- hde extension loaded → editor awareness + live errors + file reload signals
- mcpbridge node in patch → full runtime control + live feedback

## implementation plan (for when we start)

1. **`VL.MCP.HDE.vl`** — a single hde extension vl file:
   - uses `VL.IO.WebSocket` server node (already ships with vvvv)
   - Session nodes to query open documents + errors
   - protocol: simple json over websocket, port 7123 (configurable as iobox)
   - distributable as a nuget or just a vl file users drop in their project

2. **mcp tools that use the bridge** (when detected, otherwise no-op):
   - `get_running_documents` → list of open .vl file paths
   - `get_vvvv_errors` → current compilation errors with file + line
   - `reload_file` → post-edit reload signal
   - `open_patch` → navigate to a .vl file

3. **`VL.MCPBridge`** (c# node, optional):
   - uses `AppHost.Global` + `ILiveDataHub` for pin access
   - uses aspnet core (already in process) for http api
   - exposes: node outputs, channel values, console log stream

## the vs debugger angle

since vvvv is a .net 8 app, any c# bridge node we write can be debugged by:
- attaching vs/vs code debugger to the `vvvv.exe` process
- setting breakpoints in the bridge node's c# source
- inspecting live values via the debugger watch window

this eliminates the usual "how do i debug my ipc server" problem. the bridge is just a normal .net class, debuggable like any other. this makes development of the bridge significantly easier than it would be for a native plugin or an external process.

## related: what the mcp should NOT try to do

- open the node browser programmatically (it's a ui, not an api)
- simulate mouse/keyboard input (fragile, os-specific)
- recompile vl documents (vvvv does this itself on file save)
- manage vvvv process lifecycle from the mcp (out of scope — multiple versions may run)
