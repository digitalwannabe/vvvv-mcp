# VL.Math.DemoLib - Usable Nodes

No package information found

## Summary
- **Total Nodes**: 27
- **Categories**: 1

### Nodes by Type
- **Operation**: 2
- **Getter**: 9
- **Process**: 2
- **Method**: 1
- **Setter**: 9
- **Class**: 3
- **Record**: 1

### Categories
- **VL.Math.Demolib**: 27 nodes

## Nodes

### VL.Math.Demolib

#### MyGenericOperation

**Type**: Operation
**Generic**: Yes

---

#### MyGenericPad

**Summary**: Gets the MyGenericPad property

**Type**: Getter
**Generic**: Yes

**Inputs**:
- `Input` (SimpleGenericClass)
  - Instance of SimpleGenericClass

**Outputs**:
- `MyGenericPad` (Object)

---

#### MyGenericProcess

**Type**: Process
**Generic**: Yes

**Inputs**:
- `Input` (Object)
- `Input 2` (Object)

**Outputs**:
- `Output` (Object)

---

#### MyOperation

**Type**: Operation

---

#### MyOtherPad

**Summary**: Gets the MyOtherPad property

**Type**: Getter
**Generic**: Yes

**Inputs**:
- `Input` (SimpleGenericClass)
  - Instance of SimpleGenericClass

**Outputs**:
- `MyOtherPad` (String)

---

#### MyPad

**Summary**: Gets the MyPad property

**Type**: Getter
**Generic**: Yes

**Inputs**:
- `Input` (SimpleGenericClass)
  - Instance of SimpleGenericClass

**Outputs**:
- `MyPad` (Boolean)

---

#### MyProcess

**Type**: Process

**Inputs**:
- `Input` (Object)
- `Input 2` (Object)

**Outputs**:
- `Output` (Float32)

---

#### SetFromLFO

**Type**: Method

**Inputs**:
- `Period` (Object)

---

#### SetMyGenericPad

**Summary**: Sets the MyGenericPad property

**Type**: Setter
**Generic**: Yes

**Inputs**:
- `Input` (SimpleGenericClass)
  - Instance of SimpleGenericClass
- `MyGenericPad` (Object)

**Outputs**:
- `Output` (SimpleGenericClass)
  - Modified instance of SimpleGenericClass

---

#### SetMyOtherPad

**Summary**: Sets the MyOtherPad property

**Type**: Setter
**Generic**: Yes

**Inputs**:
- `Input` (SimpleGenericClass)
  - Instance of SimpleGenericClass
- `MyOtherPad` (String)

**Outputs**:
- `Output` (SimpleGenericClass)
  - Modified instance of SimpleGenericClass

---

#### SetMyPad

**Summary**: Sets the MyPad property

**Type**: Setter
**Generic**: Yes

**Inputs**:
- `Input` (SimpleGenericClass)
  - Instance of SimpleGenericClass
- `MyPad` (Boolean)

**Outputs**:
- `Output` (SimpleGenericClass)
  - Modified instance of SimpleGenericClass

---

#### SetSomeOtherPad

**Summary**: Sets the SomeOtherPad property

**Type**: Setter

**Inputs**:
- `Input` (SimpleRecord)
  - Instance of SimpleRecord
- `SomeOtherPad` (String)

**Outputs**:
- `Output` (SimpleRecord)
  - Modified instance of SimpleRecord

---

#### SetSomeOtherPad

**Summary**: Sets the SomeOtherPad property

**Type**: Setter

**Inputs**:
- `Input` (SimpleClass)
  - Instance of SimpleClass
- `SomeOtherPad` (String)

**Outputs**:
- `Output` (SimpleClass)
  - Modified instance of SimpleClass

---

#### SetSomeOtherPad

**Summary**: Sets the SomeOtherPad property

**Type**: Setter

**Inputs**:
- `Input` (SimpleClassAsProcess)
  - Instance of SimpleClassAsProcess
- `SomeOtherPad` (String)
  - another example pad as string

**Outputs**:
- `Output` (SimpleClassAsProcess)
  - Modified instance of SimpleClassAsProcess

---

#### SetSomePad

**Summary**: Sets the SomePad property

**Type**: Setter

**Inputs**:
- `Input` (SimpleRecord)
  - Instance of SimpleRecord
- `SomePad` (Boolean)

**Outputs**:
- `Output` (SimpleRecord)
  - Modified instance of SimpleRecord

---

#### SetSomePad

**Summary**: Sets the SomePad property

**Type**: Setter

**Inputs**:
- `Input` (SimpleClass)
  - Instance of SimpleClass
- `SomePad` (Boolean)

**Outputs**:
- `Output` (SimpleClass)
  - Modified instance of SimpleClass

---

#### SetSomePad

**Summary**: Sets the SomePad property

**Type**: Setter

**Inputs**:
- `Input` (SimpleClassAsProcess)
  - Instance of SimpleClassAsProcess
- `SomePad` (Boolean)
  - just some example boolean

**Outputs**:
- `Output` (SimpleClassAsProcess)
  - Modified instance of SimpleClassAsProcess

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

**Tags**: demo, lib, example, pads

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

**Summary**: Gets the SomeOtherPad property

**Type**: Getter

**Inputs**:
- `Input` (SimpleRecord)
  - Instance of SimpleRecord

**Outputs**:
- `SomeOtherPad` (String)

---

#### SomeOtherPad

**Summary**: Gets the SomeOtherPad property

**Type**: Getter

**Inputs**:
- `Input` (SimpleClass)
  - Instance of SimpleClass

**Outputs**:
- `SomeOtherPad` (String)

---

#### SomeOtherPad

**Summary**: Gets the SomeOtherPad property

**Type**: Getter

**Inputs**:
- `Input` (SimpleClassAsProcess)
  - Instance of SimpleClassAsProcess

**Outputs**:
- `SomeOtherPad` (String)
  - another example pad as string

---

#### SomePad

**Summary**: Gets the SomePad property

**Type**: Getter

**Inputs**:
- `Input` (SimpleRecord)
  - Instance of SimpleRecord

**Outputs**:
- `SomePad` (Boolean)

---

#### SomePad

**Summary**: Gets the SomePad property

**Type**: Getter

**Inputs**:
- `Input` (SimpleClass)
  - Instance of SimpleClass

**Outputs**:
- `SomePad` (Boolean)

---

#### SomePad

**Summary**: Gets the SomePad property

**Type**: Getter

**Inputs**:
- `Input` (SimpleClassAsProcess)
  - Instance of SimpleClassAsProcess

**Outputs**:
- `SomePad` (Boolean)
  - just some example boolean

---

