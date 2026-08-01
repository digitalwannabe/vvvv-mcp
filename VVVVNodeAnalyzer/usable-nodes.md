# VvvvPluginAnalyzer - Usable Nodes

## Summary
- **Total Nodes**: 36
- **Categories**: 1

### Nodes by Type
- **Operation**: 2
- **Getter**: 9
- **Process**: 2
- **Setter**: 9
- **Method**: 10
- **Class**: 3
- **Record**: 1

### Categories
- **VL.Math.Demolib**: 36 nodes

## Nodes

### VL.Math.Demolib

#### MyGenericOperation

**Type**: Operation
**Generic**: Yes

**Inputs**:
- `Input` (Object)
- `Input 2` (Object)

**Outputs**:
- `Output` (Object)

---

#### MyGenericPad

**Summary**: Gets MyGenericPad from SimpleGenericClass

**Type**: Getter
**Generic**: Yes

**Inputs**:
- `SimpleGenericClass` (SimpleGenericClass)
  - Input SimpleGenericClass

**Outputs**:
- `MyGenericPad` (Object)

---

#### MyGenericProcess

**Type**: Process
**Generic**: Yes
**Has State**: Yes

**Inputs**:
- `Input` (T)
- `Input 2` (T2)

**Outputs**:
- `Output` (Object)

---

#### MyOperation

**Type**: Operation

**Inputs**:
- `Input` (Object)
- `Input 2` (Object)

**Outputs**:
- `Output` (Int2)

---

#### MyOtherPad

**Summary**: Gets MyOtherPad from SimpleGenericClass

**Type**: Getter
**Generic**: Yes

**Inputs**:
- `SimpleGenericClass` (SimpleGenericClass)
  - Input SimpleGenericClass

**Outputs**:
- `MyOtherPad` (String)

---

#### MyPad

**Summary**: Gets MyPad from SimpleGenericClass

**Type**: Getter
**Generic**: Yes

**Inputs**:
- `SimpleGenericClass` (SimpleGenericClass)
  - Input SimpleGenericClass

**Outputs**:
- `MyPad` (Boolean)

---

#### MyProcess

**Type**: Process
**Has State**: Yes

**Inputs**:
- `Input` (Object)
- `Input 2` (Object)
- `Input3` (Object)
- `Input 4` (Object)
- `Input 5` (Object)
- `Pause` (Object)

**Outputs**:
- `Output` (Float32)
- `Output2` (Object)
- `Output3` (Object)

---

#### Set MyGenericPad

**Summary**: Sets MyGenericPad on the SimpleGenericClass instance

**Type**: Setter
**Generic**: Yes

**Inputs**:
- `SimpleGenericClass` (SimpleGenericClass)
  - Input SimpleGenericClass
- `MyGenericPad` (Object)

**Outputs**:
- `Output` (SimpleGenericClass)
  - Modified SimpleGenericClass

---

#### Set MyOtherPad

**Summary**: Sets MyOtherPad on the SimpleGenericClass instance

**Type**: Setter
**Generic**: Yes

**Inputs**:
- `SimpleGenericClass` (SimpleGenericClass)
  - Input SimpleGenericClass
- `MyOtherPad` (String)

**Outputs**:
- `Output` (SimpleGenericClass)
  - Modified SimpleGenericClass

---

#### Set MyPad

**Summary**: Sets MyPad on the SimpleGenericClass instance

**Type**: Setter
**Generic**: Yes

**Inputs**:
- `SimpleGenericClass` (SimpleGenericClass)
  - Input SimpleGenericClass
- `MyPad` (Boolean)

**Outputs**:
- `Output` (SimpleGenericClass)
  - Modified SimpleGenericClass

---

#### Set SomeOtherPad

**Summary**: Returns a new SimpleRecord with SomeOtherPad replaced

**Type**: Setter

**Inputs**:
- `SimpleRecord` (SimpleRecord)
  - Input SimpleRecord
- `SomeOtherPad` (String)

**Outputs**:
- `Output` (SimpleRecord)
  - New SimpleRecord with SomeOtherPad set

---

#### Set SomeOtherPad

**Summary**: Sets SomeOtherPad on the SimpleClass instance

**Type**: Setter

**Inputs**:
- `SimpleClass` (SimpleClass)
  - Input SimpleClass
- `SomeOtherPad` (String)

**Outputs**:
- `Output` (SimpleClass)
  - Modified SimpleClass

---

#### Set SomeOtherPad

**Summary**: Sets SomeOtherPad on the SimpleClassAsProcess instance

**Type**: Setter

**Inputs**:
- `SimpleClassAsProcess` (SimpleClassAsProcess)
  - Input SimpleClassAsProcess
- `SomeOtherPad` (String)
  - another example pad as string

**Outputs**:
- `Output` (SimpleClassAsProcess)
  - Modified SimpleClassAsProcess

---

#### Set SomePad

**Summary**: Returns a new SimpleRecord with SomePad replaced

**Type**: Setter

**Inputs**:
- `SimpleRecord` (SimpleRecord)
  - Input SimpleRecord
- `SomePad` (Boolean)

**Outputs**:
- `Output` (SimpleRecord)
  - New SimpleRecord with SomePad set

---

#### Set SomePad

**Summary**: Sets SomePad on the SimpleClass instance

**Type**: Setter

**Inputs**:
- `SimpleClass` (SimpleClass)
  - Input SimpleClass
- `SomePad` (Boolean)

**Outputs**:
- `Output` (SimpleClass)
  - Modified SimpleClass

---

#### Set SomePad

**Summary**: Sets SomePad on the SimpleClassAsProcess instance

**Type**: Setter

**Inputs**:
- `SimpleClassAsProcess` (SimpleClassAsProcess)
  - Input SimpleClassAsProcess
- `SomePad` (Boolean)
  - just some example boolean

**Outputs**:
- `Output` (SimpleClassAsProcess)
  - Modified SimpleClassAsProcess

---

#### SetFromLFO

**Type**: Method

**Inputs**:
- `Period` (Object)

---

#### SetMyGenericPad

**Type**: Method
**Generic**: Yes

**Inputs**:
- `MyGenericPad` (Object)

---

#### SetMyOtherPad

**Type**: Method
**Generic**: Yes

**Inputs**:
- `MyOtherPad` (Object)

---

#### SetMyPad

**Type**: Method
**Generic**: Yes

**Inputs**:
- `MyPad` (Object)

---

#### SetSomeOtherPad

**Type**: Method

**Inputs**:
- `SomeOtherPad` (Object)

---

#### SetSomeOtherPad

**Type**: Method

**Inputs**:
- `SomeOtherPad` (Object)

---

#### SetSomeOtherPad

**Type**: Method
**Has State**: Yes

**Inputs**:
- `SomeOtherPad` (Object)

---

#### SetSomePad

**Type**: Method

**Inputs**:
- `SomePad` (Object)

---

#### SetSomePad

**Type**: Method

**Inputs**:
- `SomePad` (Object)

---

#### SetSomePad

**Summary**: this op only sets some pad

**Remarks**: this is a sample remark

**Tags**: setter,boolean

**Type**: Method
**Has State**: Yes

**Inputs**:
- `SomePad` (Object)
  - the input value

---

#### SimpleClass

**Type**: Class

**Inputs**:
- `SomePad` (Boolean)
- `SomeOtherPad` (String)

**Outputs**:
- `Output` (SimpleClass)
  - Instance of SimpleClass

---

#### SimpleClassAsProcess

**Summary**: this patch shows a simple class used as process node

**Remarks**: the patch itslef doesnt do much

**Tags**: demo,lib,example,pads

**Type**: Class
**Has State**: Yes

**Inputs**:
- `SomePad` (Boolean)
  - just some example boolean
- `SomeOtherPad` (String)
  - another example pad as string

**Outputs**:
- `Output` (SimpleClassAsProcess)
  - Instance of SimpleClassAsProcess

---

#### SimpleGenericClass

**Type**: Class
**Generic**: Yes

**Inputs**:
- `MyPad` (Boolean)
- `MyOtherPad` (String)
- `MyGenericPad` (Object)

**Outputs**:
- `Output` (SimpleGenericClass)
  - Instance of SimpleGenericClass

---

#### SimpleRecord

**Type**: Record

**Inputs**:
- `SomePad` (Boolean)
- `SomeOtherPad` (String)

**Outputs**:
- `Output` (SimpleRecord)
  - Instance of SimpleRecord

---

#### SomeOtherPad

**Summary**: Gets SomeOtherPad from SimpleRecord

**Type**: Getter

**Inputs**:
- `SimpleRecord` (SimpleRecord)
  - Input SimpleRecord

**Outputs**:
- `SomeOtherPad` (String)

---

#### SomeOtherPad

**Summary**: Gets SomeOtherPad from SimpleClass

**Type**: Getter

**Inputs**:
- `SimpleClass` (SimpleClass)
  - Input SimpleClass

**Outputs**:
- `SomeOtherPad` (String)

---

#### SomeOtherPad

**Summary**: Gets SomeOtherPad from SimpleClassAsProcess

**Type**: Getter

**Inputs**:
- `SimpleClassAsProcess` (SimpleClassAsProcess)
  - Input SimpleClassAsProcess

**Outputs**:
- `SomeOtherPad` (String)
  - another example pad as string

---

#### SomePad

**Summary**: Gets SomePad from SimpleRecord

**Type**: Getter

**Inputs**:
- `SimpleRecord` (SimpleRecord)
  - Input SimpleRecord

**Outputs**:
- `SomePad` (Boolean)

---

#### SomePad

**Summary**: Gets SomePad from SimpleClass

**Type**: Getter

**Inputs**:
- `SimpleClass` (SimpleClass)
  - Input SimpleClass

**Outputs**:
- `SomePad` (Boolean)

---

#### SomePad

**Summary**: Gets SomePad from SimpleClassAsProcess

**Type**: Getter

**Inputs**:
- `SimpleClassAsProcess` (SimpleClassAsProcess)
  - Input SimpleClassAsProcess

**Outputs**:
- `SomePad` (Boolean)
  - just some example boolean

---

