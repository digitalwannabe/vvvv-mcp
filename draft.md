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