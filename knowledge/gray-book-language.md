<!-- gray-book section: Language (VL) -->
# Gray Book — Language (VL)

> Source: https://thegraybook.vvvv.org/ (CC-licensed)

---
<!-- page: cache.md -->

# The Cache region

To prevent parts of a patch being executed every frame, you can use a `Cache` region. The number one use-case for Cache regions is optimizing performance by making sure things only get executed when they really need to, thus saving precious CPU cycles.

![](../../images/language/cache-region.png)

All nodes inside a Cache region are only executed if one of its border control inputs is changed or its `Force` input is set to true.

Once executed, the regions output border control points hold (ie. cache) the results until the region is executed again. 

The `Dispose Cached Outputs` input defines whether objects, cached in one of the regions output border control points, will be disposed, before a new result is being cached. As a rule of thumb: If the objects class has a Dispose() method, you'll most likely want to activate this input, except you're intentionally dealing with its disposal in a different way. 

The `Has Changed` output returns true for every frame the the region was executed.

You can quickly surround a bunch of nodes with a Cache region, by selecting them and then choosing `Surround -> Cache` from the rightclick context menu.

Moving nodes into and out of the region works by pressing <span class="keyseq"><kbd>SPACE</kbd></span> while dragging them.

---
<!-- page: categories.md -->

# Categories

Categories in VL are synonymous to "namespaces" in other programming languages. They allow you to structure your libraries of nodes. 

## A documents category
Every VL document starts a category which can be defined in its Definitions Patch. 

![](../../images/language/07_DocPatch.png)
<center>"Voo" specified as a documents category</center>

## Category elements
Category elements can be added to the [Definitions Patch](patches.md#definitions-patch) via the NodeBrowser by choosing "Category", to build a category structure that holds different parts of a library. 

A categories name appends itself to the category of its parent patch. That way you can build up any category hierarchy, that you then see in the NodeBrowser. Multiple category levels are allowed with dot notation. e.g. _MyCat1.MyCat2_ etc.

![](../../images/language/03_CategoryOutside.PNG)
<center>Category patch from the outside</center>

![](../../images/language/04_CategoryInside.PNG)
<center>Inside a category patch</center>

## Full Category
A Full Category is similar to a normal Category, only that it doesn't add its category to the parent but starts a new root category. 

![](../../images/language/05_FullCategoryOutside.PNG)
<center>Category patch from the outside</center>

![](../../images/language/06_FullCategoryInside.PNG)
<center>Inside a category patch</center>

> [!NOTE]
> Empty categories are not showing-up in the NodeBrowser.

## Changing the Patch Type
You can easily convert a category into a [Group](groups.md) patch and vice versa using the patch type enum. Note how the label changes and represents the actual category structure:

![](../../images/language/08_ChangePatchType.gif)
<center>Converting a group into a category</center>

## Setting Categories on Definitions
As if the above didn't offer enough options already there is one more way to specify a category for an operation or a datatype definition:

![](../../images/language/09_SetCategoryOnDef.gif)
<center>Setting a Category on an datatype or operation definition</center>

---
<!-- page: compilation.md -->

# Compilation

Everytime you make a change in a patch, vvvv compiles it on-the-fly and updates your running programm accordingly. We call this "Hotswap" and yes, this is similar to what you may know as [.NET Hot Reload](https://devblogs.microsoft.com/dotnet/introducing-net-hot-reload/). Only that in vvvv it happens automatically, always, and you'll most often not notice it. 

When the compiler is active, you'll see a little indicator in the top left corner of the editor, right below the Quad menu:

Color of Indicator|Meaning
-|-
Gray|Building Symbols
Orange|Emitting C# code

On-the-fly compilation, while often not noticable at all, can cause severe lag, when working on large projects or libraries if all .vl files have to be considered for changes all the time. Therefore vvvv gamma 5.0 introduced the idea of read-only packages.

## Read-only packages
Patches in read-only packages are excluded from on-the-fly compilation. Like this, they run optimized in the same way as when you export them. Apart from faster execution, the fact that the compiler doesn't have to worry about them saves CPU cycles while working and also leads to a smaller overall memory footprint of vvvv which in turn removes stress from the garbage collector. 

### Restrictions in read-only packages
Patches part of a read-only package can be recognized by this banner:

![](../../images/reference/language/readonly-package-banner.png)

In read-only patches, beware of the following restrictions:
- Tooltips will not show any data flowing in the patch
- Any modification you make is not being taken into account

If you do make changes and save the patch, those changes will only be detected on next startup of vvvv, which will trigger a one-time re-compilation of the patch. 

### What makes a package read-only?
By default all packages are read-only. Like this the startup time and memory usage of vvvv is significantly improved.

This includes:
- All packages shipping with vvvv
- All [packages you install](../hde/managing-nugets.md) in addition and reference in your project. This makes sense since you're never supposed to change those other than by getting a different version of a NuGet
- All packages you reference from a [source package-repository](../extending/contributing.md#source-package-repositories)

### Editable packages
The most likely reason you'd want to opt out of the read-only default for certain packages, is when you have referenced them via a [source package-repository](../extending/contributing.md#source-package-repositories) to actually work on them. 

In this case you need to use the [commandline argument](../hde/commandline-arguments.md) `editable-packages` when starting vvvv. Here is an example to opt out of precompilation for all packages starting with "VL.Devices" and the package "VL.Audio": 

    --editable-packages VL.Devices*;VL.Audio

> [!NOTE]
> In addition this makes any package editable that depends on those you specify!

---
<!-- page: conditions.md -->

# Conditions

At this point, the only conditional language primitive in VL is the ``If`` region.

## The If region

The If region can be used to conditionally execute parts of a patch. If the ``Condition`` input is set to true, then the patch inside the region is executed, otherwise the values on its border control inputs are passed through to their corresponding outputs. 

![](../../images/language/if-region.png)

You can quickly surround a bunch of nodes with an If region, by selecting them and then choosing ``Surround -> If`` from the rightclick context menu.

Moving nodes into and out of the region works by pressing ``Space`` while dragging them. 

## Switch

While there is no dedicated Switch region, there is a Switch node that at least lets you route multiple potential inputs to one output, depending on a condition or index.

![](../../images/language/switch-node.png)

The Switch node has a pingroup for its inputs. Select it and press <kbd>Ctrl</kbd><kbd>+</kbd> or <kbd>Ctrl</kbd><kbd>-</kbd> to add/remove inputs.

---
<!-- page: delegates.md -->

# Delegates
Delegates are anonymous operations which can be passed around as an object and can be invoked on data when needed.

The fact that a delegate doesn't have a name is a feature here: as long as it has the correct signature it will fit, like a lego piece with the right shape. This way you can treat behaviour as an object which can be used to easily switch between different functionalities - without changing the calling code downstream. The delegate abstracts away the internals and only presents a facade.

Delegates have zero to many inputs, and zero or one output. This is part of what defines the signature, or "shape". They are a standard .NET feature.

Inputs are called parameters in the definition, and on the invocation side the values passed in are usually called arguments. 

## Defining a delegate
A delegate is defined using the Delegate region. It will initially be empty, so you will want to add inputs and/or outputs to have it actually do something. Here we have a delegate which takes two parameters, multiplies them, and then outputs it. Note that this code is not yet being executed.

![A Delegate](/images/language/delegates_delegate.png)

We can assign types to the in/outputs by right-clicking on them and selecting "Configure". Here we have set the first input as a `Float32` as denoted by the white dot, which will propagate to any other applicable generic types.

![A Delegate with type assigned](/images/language/delegates_delegate_typed.png)

## Invoking a delegate
To actually execute the code we have written, we need to invoke the delegate. On the output pin of the delegate, you will notice the type is something like `(T1, T2 ...) -> (T)`. You will need to match the number of parameters to the signature of the Invoke node you use.

![Different variants of the Invoke node](/images/language/delegates_invoke_variants.png)

You can now execute the delegate and pass it parameters through the Invoke node. Note that this specific delegate has no types specified - they got inferred via the usage.

![Using the Invoke node](/images/language/delegates_invoke.png)

As delegates are objects, you can for instance store them in a spread and choose which to execute programmatically.

![Delegates stored in a spread](/images/language/delegates_spread.png)

---
<!-- page: enumerations.md -->

# Enumerations

An enumeration type (or enum type) is a value type defined by a set of named constants. VL has two different types of enumerations: 
- Static Enums
- Dynamic Enums

## Static Enums
Entries of a static enum are fixed and cannot change at runtime. An example would be the type `LinearSpreadAlignment`.

### Using Static Enums
For working with static enums, use nodes from the `Primitive.Enum` category. 

![](../../images/language/static-enums.png)

### Defining Static Enums
As of now static enums cannot be created in VL itself. Instead you have to create a small C# code snippet that defines the enum. Follow the instructions unter [writing nodes](../extending/writing-nodes.md) and choose the "Static Enum" template. Opening the .cs file the template created, you'll see the following line:

```csharp
    public enum StaticEnum { foo, bar };
```

You can customize this line to your needs, like e.g: 

```csharp
    public enum MyEnum { a, b, c };
```

Saving the .cs file will make the enum available in your VL document. 

## Dynamic Enums
A dynamic enum can have entries added, removed or changed during runtime. They are used for example for device enumerations. 

### Using Dynamic Enums
For working with dynamic enums, use nodes from the (advanced) `Primitive.DynamicEnum` and `Primitive.DynamicEnumDefinition` category.

![](../../images/language/dynamic-enums.png)

### Defining Dynamic Enums
As of now dynamic enums cannot be created in VL itself. Instead you have to create a small C# code snippet that defines the enum. Follow the instructions unter [writing nodes](../extending/writing-nodes.md) and choose one of the "Dynamic Enum" templates. Opening the .cs file the template created, you can customize the template. 

For more details on this customization, see: [Defining Dynamic Enums](../extending/writing-nodes.md#dynamic-enums).

After your changes, saving the .cs file will make the enum available in your VL document.

---
<!-- page: exception-handling.md -->

# Exception Handling

Nodes sometimes get a pink border, which means that they are throwing a runtime error. Hovering the node with the tooltip will show you more information about the error:

![](../../images/language/node-throwing-error.png)

Depending on your [Setting](../hde/settings.md) for "Pause on Error" the execution of your patch will either pause or continue. But even if the execution continues it may fail again at the same spot at a later time. Therefore it is good practise, to "handle" exceptions. 

In order to handle runtime errors programmatically, you can surround the culprit with a Try region:

![](../../images/language/try-region.png)

This will allow you to react to problems in your patch gracefully without interrupting the execution of your program.

---
<!-- page: execution-order.md -->

# Execution Order

In most cases the order of execution of nodes is obvious: From top to bottom, in the order they are connected with each other through links. 

## No link dependency
There are cases though, where there is no link dependency between nodes that would express an order of execution. This may be fine or not, depending on your use case. Here are some cases where this may matter and how to solve them:

### Multiple writes to mutable data

When writing to (ie modifying) a mutable datatype several times in one frame, execution order typically matters. In this example the Value is read but the fact that it is reading "1.00" for this frame is not defined, it could be "0.00" as well, since there is no order defined. This is also what the "yellow socks" warning on the links is about.

![](../../images/language/mutable-undefined-order.png)

Instead of connecting the operations to the pad "in parallel", connect them "in series", and thus specifying a well defined order of execution. 

![](../../images/language/mutable-defined-order.png)

### Nodes with no connection in the patch

If you want to write data to a file and read it in the same frame, you need to make sure to write before you read. A naive patch like this, wouldn't make sure of this and thus might randomly work, or not:

![](../../images/language/writer-reader-undefined.png)

In order to create links between nodes in such situations, you can use the `Do` region. The region itself does nothing but letting you create input and outputs for it so you can use those to define an order of execution.

![](../../images/language/writer-reader-defined.png)

### Nodes without any pins

There are cases of nodes that don't have any pins at all. Often those are operations to globally initialize the state of a library, in which case it is important to have them execute before anything else. In those situations a `Do` region can help you build an order of execution, like so:

![](../../images/language/nodes-without-pins.png)

## Circular graphs

When trying to make a circular link connection, VL will prevent you from doing so. If you force the link by pressing <span class="keyseq"><kbd>Space</kbd></span> while making the connection, you'll see an error like this:

![](../../images/language/cyclic-graph-error.png)

Think about it: If VL would allow you to do this, it would never know where to start the execution. Therefore in such situations you need to find out where to best store the value of a computation from a previous frame and use it in the next frame. 

To solve this, introduce a [Property](properties.md) and use Pads to write a value in one frame and read it in the next frame:

![](../../images/language/property-instead-of-cyclic-graph.png)

---
<!-- page: frames.md -->

# Frames

Frames help you structure your patches visually. You can put a frame behind parts of your patch and give it a title and color.

For an overview of all keyboard shortcuts related to frames, see [Frame Shortcuts](../hde/keyboard-shortcuts.md#Frames).

![](../../images/language/frame.png)

When selected, the frame can be tinted using one of the predefined colors:
![](../../images/language/frame-selected.png)

You can move a frame around without its content, by dragging the gray bar (when selected). To move a frame and its content, drag it on its title.

## Screenshots

Besides being structural elements, frames also allow you to take screenshots easily and repeatably:

* Press the Printer button to make a screenshot, then rightclick it to see the captured file in explorer
* Alternatively press <kbd>Ctrl</kbd><kbd>2</kbd> to take a shot of the selected frame
* Press <kbd>Ctrl</kbd><kbd>5</kbd> to take screenshots of all frames in a document at once

To create a quick screenshot of an area without even creating a frame, simply press <kbd>S</kbd> while making a selection. This will copy the screenshot to the clipboard (so you can simply paste it into the chat or a forum reply) and also place a .png next to the current .vl document.

## Recordings

Apart from single screenshots you can also record an animated gif of the area of a frame:

* Press the Record button to start a recording, the same button again or <kbd>Esc</kbd> to stop it, then rightclick it to see the recorded file in explorer
* Alternatively toggle <kbd>Ctrl</kbd><kbd>4</kbd> to start/stop recording the selected frame

---
<!-- page: generics.md -->

# Generics

---
<!-- page: groups.md -->

# Groups

Groups in VL help you structure elements visually but they don't have any meaning to the language, like [Categories](categories.md) do. So you can use Groups to make extra space on a patch by hiding elements away in a new patch without adding to the category structure. 

Group elements can be added to the [Definitions Patch](patches.md#definitions-patch) via the NodeBrowser, by choosing "Group". Just like Categories, Groups can also be nested.

![](../../images/language/02_GroupInside.PNG)
<center>Inside a group patch</center>

![](../../images/language/01_GroupOutside.PNG)
<center>Group patch from the outside</center>

A group can easily be converted to a [Category](categories.md):

![](../../images/language/08_ChangePatchType.gif)
<center>Converting a group into a category</center>

---
<!-- page: ioboxes.md -->

# IOBoxes

Short for "Input/Output boxes", they allow you to _input_ constant data into your program or _output_ it for debugging or display purposes.

![](../../images/language/ioboxes-8e444.png)
<center>Some IOBoxes of different types</center>

You typically create an IOBox by starting a link from an input or output pin and then using a Middleclick (or <span class="keyseq"><kbd>ALT</kbd></span> + leftclick) to create the according IOBox. Alternatively you can create an IOBox by selecting one from the nodebrowser popping up when you right doubleclick in the patch.

![](../../images/language/ioboxes-fb5fa.png)
<center>Choose an IOBox from the nodebrowser after double rightclick</center>

An IOBox exists for each primitive datatype and they all have some special features which you can learn about here:

## Configuring IOBoxes
Configuring IOBoxes via their inspector works the same for all of them:

* Middleclick the IOBox
* or Rightclick its label and select `Configure`.

![](../../images/language/ioboxes-e0989.png)
<center>Configuring a number IOBox</center>

## Numbers

Number IOBoxes work the same for whole (integer32, byte, ...) and real (float32, ...) numbers:

* Doubleclick to enter a value via keyboard

> [!NOTE]
> You can also enter math formulas like, e.g.: "1/3" that will be immediately be evaluated and fill the IOBox with the result.  
> For a signchange like "-1" you'll have to write "±1"!

* Right-drag up and down to change the value gradually
  * hold <span class="keyseq"><kbd>SHIFT</kbd></span> while dragging, to divide the step-size by 10
  * hold <span class="keyseq"><kbd>CTRL</kbd></span> while dragging, to divide the step-size by (another) 10
  * hold <span class="keyseq"><kbd>ALT</kbd></span> in combination with the above to multiply instead of divide the stepsize
* <span class="keyseq"><kbd>ALT</kbd></span> + Rightclick to reset the value to its default

Via the inspector you can configure the IOBox:

* Choose minimum and maximum and stepsize that will be taken into account when right-draging the value
* Choose a display precision
* Choose to display a unit for the value. The unit has no effect on the value and is only good for display purposes
* Choose to show a horizontal or vertical slider

## Vectors
For Vector IOBoxes you can foremost configure whether you want to view their individual components in a vertical or horizontal stack.

![](../../images/language/ioboxes-d3c5e.png)
<center>A 2d vector IOBox with the all-components editor visible</center>

Also you can change all components at once by changing the value popping up above the IOBox when hovering it.

## Booleans
A boolean IOBox has three different button modes:

![](../../images/language/ioboxes-6d217.png)
<center>The three modes of a boolean IOBox</center>

* Toggle: a rightclick toggles between TRUE and FALSE
* Bang: a rightclick makes it return TRUE for one frame, otherwise returns FALSE
* Press: returns TRUE as long as it is right-pressed, otherwise false

## Strings

Changing values in string IOBoxes works as follows:

* Doubleclick to enter text via the keyboard
  * While entering text press <span class="keyseq"><kbd>CTRL</kbd><kbd>ENTER</kbd></span> to add a new line
* <span class="keyseq"><kbd>CTRL</kbd></span> + Rightclick to open the file chooser dialog
* <span class="keyseq"><kbd>SHIFT</kbd></span> + Rightclick to open the directory chooser dialog

Via the inspector you can configure the IOBox:

* Choose one of three string types:
  * *String*: the default
  * *Comment*: to put a comment in a patch
  * *Link*: to put a link as comment in a patch that open in the browser on rightclick
* Choose to visualize non-printable characters (ie. those with an ascii code < 32)

![](../../images/language/ioboxes-4e18c.png)
<center>The three different types of string IOBoxes</center>

## Colors
Color IOBoxes let you enter colors in many different ways:

* Doubleclick to enter the [name of a color](https://docs.microsoft.com/en-us/dotnet/api/system.windows.media.colors?view=netframework-4.8) via the keyboard
* Doubleclick to enter the values of a color in different formats:
  * a string of type: "H:0.00 S:0.00 V:1.00 A:1.00" where each component (hue, saturation, value and alpha of the HSVA color model) is a value between 0 and 1
  * a string of type: "R:0 G:255 B:0 A:255" where each component (red, green, blue and alpha of the RGBA color model) is a value between 0 and 255
  * a string of type: "RRGGBBAA" where each of RR, GG, BB and AA are pairs of hexadecimal values from 0 to 255, eg for red specify: "FF0000FF"

Change colors via mouse interaction:

Description | Action
-|-
Change brightness|Rightdrag up/down
Change hue|Rightdrag left/right
Change saturation|<span class="keyseq"><kbd>Ctrl</kbd></span> + Rightdrag up/down
Change the alpha channel|<span class="keyseq"><kbd>Shift</kbd></span> + Rightdrag up/down

## Paths

Path IOBoxes can be used to enter filenames or directories. By default they always assume you want to choose a filename!

> [!NOTE]
> Path IOBoxes always store relative paths if possible but actually hide this fact from you! This will lead to confusion in the rare case that you actually want to specify an absolut path: While IOBox and tooltip will show the absolut path you entered, internally a relative path is stored. So if you really need to specify an absolut path, use a string IOBox followed by a ToPath [IO] node.

* Rightclick to open open the file chooser dialog
* <span class="keyseq"><kbd>SHIFT</kbd></span> + Rightclick to open the directory chooser dialog
* Click the [O] icon to open the currently selected file with its associated application
* <span class="keyseq"><kbd>ALT</kbd></span> + click the [O] icon to view the file/directory in windows explorer

Via the inspector you can configure the IOBox:

* Choose between *File* or *Directory* as path type which simply determines which dialog a rightclick on the IOBox will pop up

## Collections
Collection IOBoxes work with all the above datatypes. Most often you'd create them automatically by starting a link from a pin that has a collection type (e.g. Spread, Sequence,...) and then middle-click to create the according IOBox automatically. 

![](../../images/language/collectioniobox.gif)
<center>Middleclick to create an IOBox</center>

If you want to manually create a collection IOBox, first create a normal IOBox and then configure its type to be of eg. `Spread<Float32>`.

![](../../images/language/collectioniobox2.gif)
<center>Annotating the type of an IOBox</center>

The number you see topleft in the IOBox specifies the number of elements in the collection and can be changed. By default a collection IOBox will display up to 5 elements. When the collection contains more items, a scrollbar will be shown.

![](../../images/language/ioboxes-08b7c.png)
<center>A spread of floats inspected via a collection IOBox</center>

Via the inspector you can configure the IOBox:

* The number of maximum visible entries
* Show/Hide the entry index
* Define whether the entries will be displayed as a vertical or horizontal stack
* Add/Remove entries

---
<!-- page: language.md -->

# The language VL

VL is the name of the language used in vvvv. Here is a quick overview:

## Language Components

* The most important components of VL are [Nodes](nodes.md) and [Links](links.md)
* Nodes and links live on a canvas called [Patch](patches.md)
* One or multiple patches are contained in a *_VL Document_*. VL Documents are stored as files on disk
* Every patch can define one or more [Operations](operations.md). Operations can be created as nodes in other patches
* There are two kinds of patches, one is merely a container for independent operations the other is a data type. If a patch is a data type its operations can store and/or share data via [Properties](properties.md). Properties can be accessed and/or modified in operations via [Pads](properties.md#pads)
* One _VL Document A_ can reference another _VL Document B_ to use its data types and operations as nodes. File B is then called a *_Dependency_* of A
* Patches can also contain *_Regions_*. A region defines a new computational context. There are different kinds of regions
* Nodes and regions can have inputs and outputs called *_Pins_*
* Static data can be entered into an operation using [IOBoxes](ioboxes.md). IOBoxes are little editors for basic types like numbers, text, color… They can also be used to display the current value of anything connected upstream.
* Links transport data from outputs to inputs. Therefore they define the data flow and execution order of the nodes in an operation.

## Multi-Paradigm

- VL combines metaphors known from dataflow, functional and object-oriented programming
- It is strictly evaluated
- It comes with regions aka visual code blocks (loops, if, delegates, ...)
- It features Process nodes aka simple lifetime management
- It has Adaptive nodes aka adhoc polymorphism

## The Type System

- VL is statically typed
- It has automagic type inference
- It has first class support for mutable and immutable datatypes
- It supports generics aka parametric polymorphism (with bounded quantification)
- And interfaces aka subtype polymorphism

---
<!-- page: links.md -->

# Links

Links are the connections between pins on which data flows from one node to another. There are 3 different kinds of links:

*Image:Normal Link, Reference Link, Delegate Link*

In [datatype patches](patches.md#datatype-patches), links can have colors, which tells you which [member operation](operations.md#member-operations) they belong to. 

A "yellow sock" on a link is a warning that the source of the link is mutable and it connected to more than one downstream node. Please read the full explanation in the tooltip to learn what this means and how you can deal with it. 

For many different actions you can do while creating a link, or on existing links, see [Link Shortcuts](../hde/keyboard-shortcuts.md#links).

---
<!-- page: loops.md -->

# Loops

There are two different Loops in VL:

* Repeat: a classic for-loop with an _Iteration Count_ input to specify the number of iterations the loop executes
* ForEach: executes for each slice of a spread entering the loop via a splicer. 

In the NodeBrowser you'll find different nodes named Repeat and ForEach. To get these primitive ones, choose the versions written in _italic_.

*Image:Choosing Repeat or ForEach from the NodeBrowser*

## Getting data into a loop
There are 3 different ways of getting data into a loop. All of them work for both the Repeat and the ForEach loop:

### Direct Connection

Data can be linked directly into a loop which results in each of the loops iterations receiving the same data.

*Image:A direct connection into a loop region*

### Splicer

Splicers allow you to access consecutive slices of a spread in consecutive iterations of a loop. Entering a loop with a link via the splicer-bar that shows up when starting a link, automatically leads to each iteration of the loop receiving one slice of the incoming spread.

*Image:A spread connects into a loop using a splicer*

Multiple spreads can go into the same loop via splicers. In case of a ForEach loop the number of its iterations is then determined by the lowest of the slice-counts of all incoming spreads! 

*Image:A foreach loop receives a spread with 20 slices and another one with 15 slices via splicers causing it to execute 15 times.*

In case of a Repeat Loop the iteration count determines the number of iterations ignoring the slice-counts of the spreads coming in via splicers. When the iteration count is higher than a spreads slice-count, slices of the spread are being repeatedly accessed with the loops index taken modulo the spreads slice-count.

*Image:A Repeat loop has its Iteration Count set to 5 and receives a spread with 2 slices and another one with 3 slices via splicers causing it to execute 5 times.*

By default splicers have no names. Sometimes it may help to label a splicer for clarity in which case you simply doubleclick the area right of it to enter a name. 

### Accumulator

Accumulators allow you to hand data over between iterations of a loop. Once initialized from outside of the region, the accumulator can be accessed and modified in each iteration and is then passed on to the next iteration of a loop. The final value is then available via the accumulator output.

An accumulator can thus be understood as a variable declared outside and then modified in each iteration of a loop. 

*Image:An accumulator is modified in each iteration.*

By default accumulators don't have a name. Only to distinguish multiple accumulators in the same loop from each other, they are automatically labeled using roman numbers. If you want to specify your own you can do so by doubleclicking the area right of an accumulator to change its name. 

## Getting data out of a loop

Since you can never link directly out of any region, there are only two different ways of getting data out of a loop. They work for both the Repeat and the ForEach loop:

Use an outgoing splicer to collect the results of all iterations and return them as one spread. 

Use an outgoing accumulator to receive the final value as modified by all iterations of a loop.

## Special Pins

There are three special Pins which you can create only inside loops via the NodeBrowser:

*Image:The Index, Break and Keep pins in a loop*

### Index 
Returns the current loop iteration number.

### Break
Set it to true to break out of the loop at any time before its actual iteration count is reached. Note that the breaking iteration is still fully executed, resulting in output splicers to include the result and accumulators to be modified by this iteration.

To find out if a loop executed to the end or it was interrupted by a break, the _Break_ output can be tested.

### Keep
Set it to true or false in each iteration to specify whether results of this iteration will be included in a spread returned by an outgoing splicer.

Note that the keep has no influence on accumulators, meaning that accumulators will still be changed for iterations that are not 'kept'.

# Other Loops
## While 
Use a Repeat loop with its _Iteration Count_ set to a very high number which in this case you can consider as the maximum iteration count you specify in order to make sure your patch can never hang indefinitely. Inside the loop you specify the condition that needs to be met for the loop to continue to execute and connect its negation to the loops _Break_ output.

*Image:Simulating a while loop using a Repeat loop*

---
<!-- page: namings.md -->

# Identifier Naming Conventions

VL uses Pascal Case as casing convention. Examples:

Data types:
<pre>
Particle
ParticleSystem
AlignedBox
</pre>

Operations:
<pre>
Update
GetPosition
SplitCurve
</pre>

In general the following characters are allowed, not starting with a number or space:

**a-z A-Z 0-9 + - * / = ~ < >**

Pads and pins should contain spaces to make them visually more pleasing and distinct from operations:
<pre>
Velocity
Map Mode
Word List
</pre>

Categories can include periods:
<pre>
Math
Collections.Spread
Animation.FrameBased  
</pre>

---
<!-- page: nodes.md -->

# Nodes

Nodes are the main building blocks of a patch. They have input Pins at the top and output Pins at the bottom. Pins are the hubs that allow Nodes to be connected via [Links](links.md). 

Nodes are also referred to as the "application" of a node definition.

## Node Name
A nodes name consists of the following components: 
- Its display name
- An optional version
- Its category (think namespace)

Hovering a node to view its tooltip shows its full name:

![](../../images/language/nodename.png)
<center>The node "Split" of version "Count" in the category "Primitive.String"</center>

## Types of Nodes
There are different types of nodes:

*Image: Process node, Static Operation node, record operation node, class operation node*

### Process Nodes
A process node represents a single instance of a patch. 

The name _Process_ comes from the fact that it can be understood like a little machine that does some initialization routine (the `Create` operation) once it is first executed and then continues to execute one or more of its operations in a loop, maintaining an inner state from one frame to the next. Typically, a Process node has at least an `Update` operation, but it is not limited to that. 

Process nodes have a distinguished look with the bars behind their pins being darker. This makes them visually heavier, hinting at the fact that they are holding state, ie. storing data between consecutive executions.

For more on defining Process Nodes, see [Datatype Patches](patches.md#process).

### Static Operation Nodes
Operation nodes are nodes representing a single operation.

They have a lighter look than Process Nodes, with the bars behind their pins not being visible. This indicates that they are not operating on a state, ie. they do not store any data between consecutive executions.

#### Apply input
If the first input and first output of a static operation node share the same datatype, they can have an _Apply_ pin added via `context-menu > Configure`. 

The Apply input defaults to true. When disabled, the operation is bypassed and the input returned unchanged as the output.

This is essentially a shortcut to surrounding the node with an [If Region](conditions.md#the-if-region).

### Record Operation Nodes

Record operation nodes are part of a record, on which they operate. They are visually higher, as they also display the name of the datatype they belong to in smaller type, below the nodes name. 

They have an optional "State Output" pin that is visually not connected to the corresponding "State Input" pin, indicating that the object going out is always a completely new object, cloned and modified from the incoming object.  

### Class Operation Nodes

Class operation nodes are part of a class, on which they operate. They are visually higher, as they also display the name of the datatype they belong to in smaller type, below the nodes name.

They always have a "State Output" pin that is visually connected to the corresponding "State Input" pin, indicating that the object coming in is the same as the object going out, only modified. 


## Optional Pins on Nodes

Nodes can have Pins that are not visible by default. Rightclick a Node and press Configure to show a little inspector that allows you to show/hide optional Pins.

## Pin groups

Some Nodes have Pin groups, which allow you to change their number of pins. 

Examples of nodes with Pin groups:
Group, Cons, +

Typically a Node has either a single input or output Pin group, in which case it pins can be added/removed by pressing <span class="keyseq"><kbd>CTRL</kbd><kbd>+</kbd></span> or <span class="keyseq"><kbd>CTRL</kbd><kbd>-</kbd></span> respectively.

For keyboard shortcuts in case of multiple Pin groups on a node, see:  [Pin group shortcuts](../hde/keyboard-shortcuts.md#pin-groups).


## Navigating to a Nodes definition

If a node is defined by a patch, you can navigate to its definition via pressing  `RightClick -> Open` on the node. Any node that spots an arrow icon has a patch behind in the same document or a document that is directly referenced as a file dependency. This patch can quickly be opened via middle-clicking the Node. 

*Image:Node with a patch behind it*

See also the [setting](../hde/settings.md) "Middleclick navigates to definition" to enable the middleclick to navigate to any patch even if it is not in the same or a referenced document. 

If a node is defined by SDSL shader code, the corresponding code editor will open. See [Editing Shaders](../libraries/3d/editing-shaders.md). 

Nodes that are defined by C# code cannot be inspected.

---
<!-- page: operations.md -->

# Operations

Operations define a simple functionality. They take input, do something with it and return a result. Operations cannot hold state, meaning they cannot store any data between consecutive calls. Data instead is stored in [Properties](properties.md). 

## Definition vs. Application

Using the term "operation" alone can be ambiguous if not from the context it will be clear whether we actually mean an "operation definition" or an "application of the operation definition" which again is synonymous with "node". In this chapter, we're using the term "operation" as shortcut for "operation definition". 

## Types of Operations

There are two different types of operations in vl:

* Member operations
* Static operations

## Member Operations
The term _member_ refers to the fact that those operations belong to and operate on the data of a datatype.

Datatypes can have many operations, most often they have at least a `Create` and an `Update` operation. To distinguish multiple member operations in a patch, VL uses colors for Pins and Links. There are three reserved colors: 
- White: for the Create operation
- Gray: for the Update operation
- Dark red: for the Dispose operation

All other colors are applied randomly from a color pallette and have no meaning whatsoever. They are only there to indicate the belonging of colored elements to a certain operation. To check which color refers to which operation, use the [Patch Explorer](../hde/patch-explorer.md) or hover the pin and find the operation mentioned in the tooltip.  

*Image:A member operation definition and its application as a node*

### Creating a Member Operation
Member operations are either created via the [Patch Explorer](../hde/patch-explorer.md), or during the assignment workflow, where you can choose to assign to a new operation and then specify the name of the operation to be created and assigned to at the same time.

### Assigning Nodes, Inputs/Outputs and Links to operations

Use the elements context menu to assign it to one of the available operations or create a new one. 

Often it makes sense to start assignments on Input or Output pins. Note that assignments auto-propagate through the whole patch. They only stop at Pads or Process Nodes, which act kind of like bridges between the operations in that they store values written by one operation and have them available for retrieval by an other operation. 

There are cases though where no Input or Output pin is part of an operation. In that case consider setting an assignment onto a link or Operation Node.

> [!NOTE]
> Process Nodes cannot be assigned to an operation. Instead you'll see that their Pins can assign to different operations, meaning that different parts (operations) of a Process Node can be executed on different operations in the containing patch. 

### Clearing Operation assignments

To remove the assignment from an element, also use the context menu -> Clear assignment. 

By default, elements with no assignment will "fall back" to being executed on Update, if it is available. 

### The Dispose Operation

If you want to get rid of an instance of an object that you've created dynamically at runtime, typically you'd simply make sure you don't have a reference to it anymore, e.g. like removing it from a list. But beware: If your object is "disposable" you'll also have to call its `Dispose` operation before loosing any reference to it. So the question remains how you can find out whether an object is disposable or not. For now the only way to know is by testing this: Try to connect Dispose [IDisposable] node to an instance of your object. If this connection is allowed, you know that the object is disposable and requires you to manually call `Dispose` on it.

If on the other hand you're looking at implementing the IDisposable interface in your own object, simple create a member operation called "Dispose" and use it like any other operation. At least for Process nodes, the system will now know to trigger this operation automatically whenever the Process node is deleted from a patch. 

## Static Operations
Static operations are on their own, operating only on data they are being fed with.

*Image:A static operation definition and its application as a node*

### Creating a Static Operation
Static operation definitions can be created via the NodeBrowser.

![](../../images/language/vl-Operations-Static-NodeBrowser.png)
<center>Choose to create an operation definition in the NodeBrowser</center>

By default, static operations have their _Is Generic_ property set to false. Errors will be shown for all inputs and outputs of the operation whose datatype is not specified or cannot be infered. To allow generic inputs, enable this toggle.

![](../../images/language/vl-Utils-StaticOperation-GenericToggle.png)
<center>The "Is Generic" toggle of an operation definition is off by default</center>

Once created, the operation definition shows up in the NodeBrowser and can now be created as a node.

![](../../images/language/vl-Operations-Static-MyOperation-NodeBrowser.png)
<center>The newly created operation can now be selected via the NodeBrowser</center>

## Input and Output Pins
Inputs and Outputs in operation definitions show up as Pins on the corresponding Node.

There are two ways of creating pins:
- With a link at hand, hold <span class="keyseq"><kbd>CTRL</kbd></span> while left-clicking
- Doubleclick to bring up the NodeBrowser, then type the name, you want the pin to have, then choose either `Input` or `Output`

### Configuring Input and Output Pins
Use a pins configuration menu to to configure it. You can reach the menu either way:
- Middleclick the pin
- Rightclick the pin and choose `Configure`

#### Annotating Inputs and Outputs
"Annotating" means to manually specify a datatype for a pin. In the configuration menu, the topmost entry allows you to specify a Type. Doubleclick the entry to edit it. 

> [!NOTE]
> Type names are case-sensitive, ie it is important that you're using correct spelling when setting a type.

#### Defaults for Inputs
When an input is annotated with a type you can also specify a default for it in the configuration menu. 

#### Visibility for Inputs and Outputs
As the creator of a node you can also decide if certain pins should not be visible by default. A reason to do so would be that the pin is of rather special interest and default usage of the node doesn't require it. 

When setting a pins visibility to `Optional` it can be shown by a user of the node, using the nodes configuration menu. If a pin is set to `Hidden` it cannot be used by a user of the node. 

#### Pin groups
Pins of type `Spread<T>`, `Array<T>`, `MutableArray<T>`, `Dictionary<string, T>` and `MutableDictionary<string, T>` can also be changed to a so called _Pin Group_. Pin Groups allow you to dynamically add/remove pins to a node. For the keyboard shortcut to do so, see [Pin Group Shortcuts](../hde/keyboard-shortcuts.md#pin-groups).

In order to make a pin into a pin group, it has to be annotated to one of the above types. Only then you can set the Pin Group flag in the configuration menu to TRUE.

![](../../images/language/PinGroup.png)

### Operation Signature
The signature of an operation allows you to define the order in which its Inputs and Outputs show up on corresponding nodes.

For static operations the signature can be opened directly on the operation definition region. The signature of member operations can be accessed via the PatchExplorer.

![](../../images/language/member-operation-signature.png)

*Locked signature of member operation "Update" in the [Patch Explorer](patch-explorer.md)*

![](../../images/language/static-operation-signature.png)

*Unlocked signature of static operation "Confine"*

By default signatures are *locked*, meaning the order of pins is defined by their left-to-right placement in the patch. In order to manually manage an operations signature you have to *unlock* it by pressing the Lock icon toggle. In an unlocked signature you can arrange pins via drag'n'drop.

The [Connect To Signature](../extending/forwarding.md#connect-to-signature) feature only works on locked signatures, where the system has full control over managing existence and order of pins.

Doubleclicking a pins name allows you to rename it. A middleclick on a pin allows you to annotate it with a type.

---
<!-- page: patches.md -->

# Patches

A "Patch" is a canvas that holds [Nodes](nodes.md), [Links](links.md) and other VL language elements. A VL document can comprise of many patches. There are two main types of patches:

* Datatype Patches
* Definition Patches

 Every VL document has two main patches that can be reached via the [Document Menu](../hde/navigating_a_project.md#active-document-menu):

* The Application patch: A special form of a datatype patch
* The Definitions patch: The root of all definition patches 

## Application patch
The main entry point of a VL document. If any nodes are placed here, they will be executed as soon as the document is opened, either directly or as a dependency of an other document.

This is typically the place where you start creating your program. You can reach this patch via the shortcut <span class="keyseq"><kbd>Alt</kbd><kbd>A</kbd></span>.

Application patches can also hold definitions, but this is not considered particularly good practice. 

In case a documents application patch is empty, the document is only used as a library, ie. only providing node definitions to any document referencing it. 

## Definitions patch
This is where all the node definitions of a VL document are placed. Here you can use [Categories](categories.md) and [Groups](groups.md) to build a hierarchy and organize your definitions. You can reach this patch via the shortcut <span class="keyseq"><kbd>Alt</kbd><kbd>Shift</kbd><kbd>A</kbd></span>.

![](../../images/language/vl-DocumentPatch.png)
<center>Section of the definitions patch of VL.CoreLib.vl</center>

Here we typically see a range of type-definitions and categories, though a document patch can also directly hold [static operations](operations.md#static-operations).

The document patch can set or omit a base category.

![](../../images/language/vl-DocumentPatch-BaseCategory.png)
<center>Document base category set to "Foo"</center>

## Datatype Patches
There are different types of datatype patches that can be switched between in the [Patch Explorer](patch-explorer.md):

* Process
* Record
* Class
* Interface
* Forward
  
Process, Record and Class patches can have [Properties](properties.md) and [Member Operations](operations.md#member-operations). Interface and Forward are a bit more special, see below.

Every datatype patch has a corresponding type-definition element in a definition patch.

![](../../images/language/vl-DatatypePatch.png)
<center>Datatypes: Process, Record and Class</center>

There are different ways to create a new datatype patch:

* In the [NodeBrowser](../hde/the_nodebrowser.md) type the name of the node you want to create and then choose `Node` to create a process node
* Press <span class="keyseq"><kbd>Ctrl</kbd><kbd>P</kbd></span> to create a process node at the cursor and open the new patch
* Press <span class="keyseq"><kbd>Ctrl</kbd><kbd>Shift</kbd><kbd>P</kbd></span> to open the new patch without creating a node application

In either case, a corresponding type-definition is automatically placed in the definitions patch of the active document.

### Process
The most common type of datatype patch is the "Process". It holds the definition for a [Process Node](nodes.md#process-nodes), ie. its life-time is bound to the existence of a node.

A processes' member operations can either be directly part of the process or not. The [Patch Explorer](patch-explorer.md) can be used to decide about this for every operation. Also the order of execution of multiple operations in a process can be configured there by dragging the operations up or down.

The Application patch of a document is a special Process patch:
* It has a Create and an Update operation but doesn't allow you to add additional operations
* It cannot be instantiated as a node, but an instance of it is running as soon as its  document is opened directly or as a dependency for another document

### Record
Defines an immutable datatype. As opposed to a Process, its life-time is not defined by the existence of a node. Instead, any number of instances of a Record can be created, update and disposed at any time. 

The typical life-time of a record goes like this:
- An instance is created using a call to its `Create` operation
- The instance is stored in a collection 
- Operations like `Update` (or others that were defined) are called on it repeatedly or only from time to time and return a new instance (replacing the previous one) that is stored in the collection again
- For this, activate the Process toggle in the [Patch Explorer](patch-explorer.md)
- The instance is removed from the collection in order to kill it. In case the record holds unmanaged resources it is also necessary to call its `Dispose` operation before removing it from the collection.

Every node that modifies a record type, essentially makes a copy of it (with changes applied) and returns a new instance. Thus, modified Records always need to be written back into a [Pad](properties.md#pads), for their changes to survive to the next frame!

The fact that a record is at anytime a fixed, immutable snapshot of data, makes it specifically suitable for use in a dataflow programming language like VL. 

A record can optionally also define a Process. For this, activate the Process toggle in the [Patch Explorer](patch-explorer.md). 

### Class
Defines a mutable datatype. Basically similar to the Record with one main difference: 

Every node that modifies a class type really modifies the original instance! No matter how far down the line a node is that operates on a class type, it is always the original instance that is being modified. So what's passed on over links, from pin to pin, is not data, but only a reference to the original instance.

A class can optionally also define a Process. For this, activate the Process toggle in the [Patch Explorer](patch-explorer.md).

### Interface
Not officially supported yet. 

### Forward
See [Forwarding](../extending/forwarding.md).

---
<!-- page: patch-explorer.md -->

# The Patch Explorer

Gives a quick overview of elements in a patch and allows to configure the patch name and type and further properties depending on the type. 

## Showing and Hiding the Explorer
By default the patch explorer is not showing. It's visibilty can be toggled by clicking the lower of the two Quad icons in the top left corner of the editor:

![](../../images/hde/patch-explorer.png)

Depending on the type of patch, the explorer shows the relevant information:

## Application Patch Explorer
![](../../images/hde/application-patch-explorer.png)

- Does not allow to specify a name
- Lists all [Properties](../language/properties.md) of the patch and allows to add/remove them
- Lists additional nested elements, like [Datatype Patch definitions](../language/patches.md#datatype-patches) and [Static Operation definitions](../language/operations.md#creating-a-static-operation)

## Definition Patch Explorer
![](../../images/hde/definition-patch-explorer.png)

- Allows to specify a Category which is applied to all elements in the document
- Lists nested elements, like [Datatype Patch definitions](../language/patches.md#datatype-patches) and [Static Operation definitions](../language/operations.md#creating-a-static-operation) and [Categories](../language/categories.md)

## Datatype Patch Explorer
### Process/Record/Class
![](../../images/hde/datatype-patch-explorer.png)

- Allows to specify the datatypes name
- Allows to set the type of [datatype patch](../language/patches.md#datatype-patches)
- For types Record/Class only: Allows to specify an [Aspect](../extending/aspects.md)
- Allows to specify whether the datatype can have generic inputs/outputs
- For types Record/Class only: Lists all Interfaces and allows to add/remove them
- Lists all [Properties](../language/properties.md) of the type and allows to add/rename/remove them
- Lists all [Member Operations](../language/operations.md#member-operations) of the type and allows to add/rename/remove them
  - On each operation the [Signature](../language/operations.md#operation-signature) can be shown and manipulated
- Allows to configure the [Process](../language/patches.md#process) Definition
  - Enable/Disable the Process 
  - Set an [Aspect](../extending/aspects.md)
  - Enable/Disable the State Output
  - Define which operations are part of the Process by toggling their checkbox 
  - Define the order of operations in the Process by dragging the operations up/down
- Lists nested elements, like [Datatype Patch definitions](../language/patches.md#datatype-patches) and [Static Operation definitions](../language/operations.md#creating-a-static-operation)

### Interface
Not officially supported yet.

### Forward
See [Forwarding .NET Libraries](../extending/forwarding.md).

## Category Patch Explorer
![](../../images/hde/category-patch-explorer.png)
- Allows to specify a name for the [Category](../language/categories.md) or [Group](../language/groups.md)
- Allows to change the type of [Category](../language/categories.md)
- Lists nested elements, like [Datatype Patch definitions](../language/patches.md#datatype-patches) and [Static Operation definitions](../language/operations.md#creating-a-static-operation) and [Categories](../language/categories.md)

---
<!-- page: properties.md -->

# Properties

Datatypes can use properties to store data. You can get an overview of the properties of a datatype via the [Patch Explorer](patch-explorer.md). 

*Image:Properties listed in the PatchExplorer*

You can add and remove properties via the [Patch Explorer](patch-explorer.md), but specifically for adding properties, you'd often simply create Pads.

If you're coming from textual programming, you may also think of properties as "variables" with the caveat that they can only be written to once per operation!

## Pads

In a patch, Pads are used to get (read data from) or set (write data to) properties. Pads refer to properties via name, meaning all Pads with the same name refer to one and the same property. Names are case-sensitive!

In every [operation](operations.md) you can assume that first always all Pads are read from. Then all its operations are executed and only as the last step all Pads are written to. 

If a link goes into a Pad (from above), data is written into this Pad. If a link leaves a Pad (at the bottom), data is read from this Pad.

Pads can have multiple links coming in and/or going out. Note though, that while multiple links can go out on the same operation, all incoming links need to be on different operations! Think about it this way: Pads cannot be used to store intermediate values during the execution of an operation. They can only be used to store data between the execution of different operations.

A little triangle above a Pad is a hint that there is a Pad with the same name in the patch that is also written to.

*Image:Different operations writing to the same Pad*

### Adding Pads
You can add Pads via [Nodebrowser](../hde/the_nodebrowser.md) in three different ways:

1) Enter the name of the Pad you want to create and then choose the entry `Pad`.
2) Choose the entry `Pad` and then enter a name
3) Choose from the list of existing properties that are listed in the Nodebrowser

### Renaming Pads
Doubleclick a Pad's name to change it. When renaming a Pad, only this one instance is renamed and eventually referring to a different property. If a property with the new name of a Pad did not exist so far, a new property is automatically added at this point!

To rename all Pads that share a name at the same time, rename the property via the [Patch Explorer](patch-explorer.md) instead. 

### Anonymous Pads
Pads without a name are called "anonymous Pads". They don't refer to a property but still allow you to store data between the call of multiple operations.

You can quickly insert an anonymous Pad into a link, by pressing <span class="keyseq"><kbd>Shift</kbd></span> while doubleclicking the link.

You can also use anonymous Pads simply as a hub to join many links into one.

*Image:Anonymous Pad use as a hub for multiple links*

## The datatype of a property
A property's datatype can either be:

1) Generic
2) Inferred 
3) Annotated
   
ad 1) Generic
By default properties are generic, meaning they don't have a datatype assigned. On Pads this is visible when they are only showing the outline of a circle. 

Properties are generic as long as none of their associated Pads are have a datatype either inferred or annotated. 

ad 2) Inferred
If the compiler has inferred a type for a Pad from the links that are connected to it, it is showing as a filled circle. You can see the inferred datatype in the Pad's tooltip by hovering it.

*Image:Generic Pad vs. Pad with a datatype inferred*

ad 3) Annotated
To set the type for a property manually, you can annotate one of its Pads. Middleclick a Pad to open a little inspector where you can edit its type. As an alternative to the middleclick you can rightclick the Pad's label and choose -> Configure.

*Image:Annotating a Pad*

You can recognize Pads that are annotated manually as they have a dot in their circle.

*Image:Annotated Pad*

## Pads vs. IOBoxes
A Pad and an IOBox are essentially the same thing: While the IOBox has a value editor and a comment (on its right side), a Pad has a name (on its left side). 

You can convert between the two via Rightclick -> Replace...

You can also enable the value editor for any Pad or hide it for any IOBox.

## Metadata 
A property can have metadata associated that adds more info to it. As an example a property of type Float32 may have Min and Max values associated that allow a UI that controls that property to constrain e.g. a slider between those two values.

Metadata can be useful for different systems looking at properties. Currently the following systems take metadata into account:
- The Object Editor: See "HowTo Build a Custom Editor" in the [Help Browser](../hde/findinghelp.md)
- The Channel Browser
- Channel Bindings

To define metadata for a property, it needs to be viewed via the [Inspector](../hde/inspector.md) by selecting a Pad that refers to it.

To read metadata programmatically, see "HowTo Reflect over Property Metadata" in the [Help Browser](../hde/findinghelp.md)

![](../../images/reference/language/pad-inspector.png)

<center><small>A property of type Float32 as seen in the Inspector.</small></center>

### Common Metadata
- Default: A value a UI can use to reset to 
Some metadata is available for all types:
- Order: The order in which this property would appear in a list compared to its siblings
- Widget: The type of widget the property would preferrably be manipulated with
- Can be Published: Whether or not the property can be published as a Channel
- Visible in ObjectEditor: Whether or not the property is visible in the ObjectEditor
- Read-Only: Whether the property can be only read or also written to
- Don't Serialize:
- Label: A human readable identifier 
- Description: A longer description for the property
- Tags: A list of terms associated with the property 

### Metadata specific for number types
- Min: Lowest allowed value (inclusive)
- Max: Highest allowed value (inclusive)

### Custom Metadata
Allows to assign custom key/value data to properties.


