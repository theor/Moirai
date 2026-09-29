# The Moirai language

A Moirai story is a `.sg` file. It says what kinds of things a world holds, what happens to them and when,
and how the world reacts when something changes. The engine then runs it one year at a time and writes the
world's history as it goes.

This guide covers how a story is put together. For each built-in function (`pick`, `record`, `chance`, …)
and each attribute (`@frequency`, `@display`, …), see the [language reference](language-reference.md). That
page is generated from the engine's code. [`MoiraiCli/w.sg`](../MoiraiCli/w.sg) is a complete story of about
a thousand lines and the best worked example.

## A first story

```moirai
enum Job { Farmer, Smith, Bard }

entity Person {
    prop alive: bool
    prop birthdate: number
    prop job: Job
    prop partner: Person
}

@start
event beginning {
    create Time $t: 'time' {
        year := 1000
    }
    create Person $p: 'Ada' {
        alive := true
        birthdate := 980
        job := Job.Smith
    }
}

@frequency(1, PerXYear, 5)
event wanderer {
    create Person $p: 'a wanderer' {
        alive := true
        birthdate := #Time.year - 20
        job := random(Job)
    }
    record('{$p.name}, a {$p.job}, arrives in {#Time.year}')
}

trigger first_words {
    when_created Person
    record('{$new.name} is here')
}
```

The `@start` event runs once, when the world is made. It sets the clock and creates Ada. Every year after
that, `wanderer` has a one-in-five chance of bringing someone new. Each new person, Ada included, sets off
the `first_words` trigger.

## Definitions

A story is a list of top-level definitions, each starting at the beginning of a line. Every definition can
be used anywhere in the file, whatever their order: a type, an enum, a table, an event or a function can be
written above or below the code that uses it.

### Entity types

```moirai
entity Country {
    prop ruler: Person
    prop prosperity: percentage
    prop neighbors: [Country]
}
```

Every entity has a `name` and an `id`, and the properties its type declares. A property's type is one of:

| Type | Values |
|---|---|
| `bool` | `true`, `false` |
| `number` | whole numbers |
| `float` | decimal numbers |
| `percentage` | `0%` to `100%`. A value outside that range is clamped when it is set. |
| `string` | text |
| an enum | `Job.Smith` |
| an entity type | a reference to an entity, or `null` |
| `[T]` | a collection: a set of values of type T. See `add`, `remove`, `contains` and `count` in the reference. |

A property that was never set reads as its type's default: `false`, `0`, `null` and so on.

A `singleton` type has at most one entity, reached as `#TypeName`. The first write to it creates it:

```moirai
singleton World {
    prop age_name: string
}
...
set #World.age_name = 'The Long Peace'
```

`Time` is built in: `#Time.year` is the clock. A story sets it in a `@start` event, as above. A story that
never mentions it starts at year 0.

A type can declare **methods**. In a method, `$self` is the entity it was called on:

```moirai
entity Person {
    prop birthdate: number
    function age(): number {
        #Time.year - $self.birthdate
    }
}
...
if $p.age() > 60 { ... }
```

Attributes placed before a type (`@display`, `@parents`, `@population`, …) tell the viewer what the
properties mean and add derived fields to an entity's page. They are listed under
[Type attributes](language-reference.md#type-attributes).

### Enums and tables

```moirai
enum Title { Commoner, Noble, King }

table Name {
    'Ada', 'Bran', 'Cora'
}
table Occupation { 70 => Job.Farmer, 20 => Job.Smith, 10 => Job.Bard }
```

`random(Title)` draws one enum value, each equally likely. A `table` is a list of values, with optional
weights, drawn with `roll(Name)`.

### Events

An event is a rule the schedule runs:

```moirai
@frequency(1, PerXYear, 2)
event wedding {
    pick Person $x: (alive and partner = null)
    pick Person $y: (alive and partner = null and $y != $x)
    set $x.partner = $y
    set $y.partner = $x
    record('{$x.name} and {$y.name} get married')
}
```

- `@start` runs the event once, when the world is created.
- `@frequency(x, PerXYear, y)` runs it at random, x times every y years on average.
- `@frequency(x, EveryXYear, y)` runs it exactly x times in every window of y years.
- With no scheduling attribute, the event runs only when a rule calls it by name.

A rule calls an event by name, `harvest()`, and runs it n times with `repeat(n) { harvest() }`. An
event can take parameters, `event found_city($founder: Person) { ... }`, called as `found_city($p)` with
each argument checked against its type. A called event runs as a rule of its own, with its own changes and
triggers. Its failure is its own too: if its `pick` finds nobody, its own changes are discarded and the
caller carries on.
Events, functions and built-ins share one set of names, so an event cannot be called `floor` or share a
name with a function.

**A rule that stops has failed.** When a statement stops the rule, typically a `pick` that finds no one,
the rule ends there. What it had already done stays in the world, records included, but its changes are
not logged as a changeset, so the history browser does not show them and no trigger reacts to them. That is
why a rule starts with its `pick`s and changes nothing until they have all found someone.
`pick … else { … }` is different. Its block runs and the rule then stops *successfully*: the block's
changes are logged, and triggers see them.

### Triggers

A trigger reacts to change. After each rule finishes, its changes are checked against every trigger:

```moirai
trigger inherit {
    when Person and alive = false and $old.alive
    set $new.deathdate = #Time.year
    record('{$new.name} dies')
}

trigger born {
    when_created Person
    set $new.birthdate = #Time.year
}
```

- `when_created T` fires for each new entity of type T.
- `when T and <predicate>` fires for each T whose change leaves the predicate true. In the predicate, a bare
  property name reads the changed entity.
- In the body, `$new` is the entity as it is now, and `$old.p` is the value p had before the change. The test
  `alive = false and $old.alive` therefore means "has just died", not "is dead".

### Functions

A top-level `function` either computes a value or runs statements:

```moirai
function is_child_of($ch: Person, $parent: Person): bool {
    $ch.parent1 = $parent or $ch.parent2 = $parent
}

function create_god() {
    create God $g: '{roll(Name)}'
    record('{$g.name} appears')
}
```

A function with a return type is an expression, and its last line is its value. It can be used in a
`pick`'s predicate, where the engine reads through it to find matches quickly. A function without a return
type is a procedure, run by name, `create_god()`, inside the calling rule.

Every function is declared before any body is read, so a function can call any other, including itself, and
an entity's methods and `@display` predicates can use top-level functions. A function that calls itself
needs a case that stops, as an event that calls itself does:

```moirai
function countdown($n: number) {
    if $n > 0 {
        record('{$n}')
        countdown($n - 1)
    }
}
```

## Statements

A rule's body is a list of statements, one per line.

| Statement | Meaning |
|---|---|
| `var $x = expression` | A local variable. Locals start with `$` and live until the end of their block. |
| `set $x.prop = expression` | Changes a property. `set #World.prop = …` writes to a singleton. |
| `create T $v: 'name' { prop := value }` | Creates an entity. |
| `pick T $v: (predicate)` | Picks one matching entity at random, or stops the rule. |
| `each T $v: (predicate) { … }` | Runs the block for every match. `each T $v { … }` visits every T. |
| `if condition { … } else { … }` | Branches. |
| `match value { case => statement … _ => … }` | Chooses by value. See below. |
| `random_weighted { weight => statement … }` | Chooses one branch at random. See below. |
| `chance(5%) { … }` | Runs the block with a probability. |
| `record('text')` | Writes a sentence into the history. |
| `name(args)` | Runs an event or a procedure now. |
| `repeat(n) { … }` | Runs the block n times. |
| `schedule($e, year) { … }` | Runs the block in a later year. |

`match` compares one or more values against each case in turn and runs the first that fits. `_` matches
anything:

```moirai
match $p.job {
    Job.Smith => record('{$p.name} forges a blade')
    Job.Bard => {
        record('{$p.name} sings')
        set $p.prestige = $p.prestige + 5%
    }
    _ => record('{$p.name} works the fields')
}

match $changed, $c.health {
    true, CountryHealth.Prosperous => record('{$c.name} prospers')
}
```

`random_weighted` picks one branch. With a total, `random_weighted 100 { … }`, each weight is out of that
total and `_` takes whatever is left. Without a total, the weights are summed, and `_` is an error:

```moirai
random_weighted 100 {
    5 => set $p.job = Job.Smith
    10 => set $p.job = Job.Bard
    _ => set $p.job = Job.Farmer
}
random_weighted {
    3 => record('A good year')
    1 => record('A hard year')
}
```

## Expressions

- **Paths**: `$p.partner.name` follows references. `#Time.year` reads a singleton, and `$p.age()` calls a
  method.
- **Comparison**: `=`, `!=`, `<`, `<=`, `>`, `>=`. Entities compare directly: `$y != $x`.
- **Arithmetic**: `+`, `-`, `*`, `/`. Percentages mix with numbers: `$p.devotion + 10%`.
- **Logic**: `and`, `or`, `not(…)`. Both stop at the first side that settles the answer, so put cheap tests
  first in a `pick`.
- **Strings** are single-quoted, and `{…}` interpolates any expression: `'{$p.name} was born in
  {#Time.year}'`. In a `record`, an entity written this way becomes a link to it.
- **Queries as values**: `count Person $p: (alive)` and `sum`, `avg`, `min` and `max` fold a query into a
  number. `if (pick T $v: (…)) { }` tests whether anything matched.

### Predicates

The parentheses of `pick`, `each`, `count` and the other queries hold a predicate over the candidate. A bare
property name reads the candidate, and `$v` is the candidate itself:

```moirai
pick Person $y: (alive and partner = null and $y != $x and place = $x.place and not(related($x, $y, 6)))
```

Commas join clauses the same way `and` does: `(alive, age > 60)`. `chance` cannot appear in a predicate:
pick first, then roll.

## Lines, comments and documentation

- A statement ends at the end of its line. A long condition can continue onto the next line only if the
  first line ends with its `and` or `or`. A line that *starts* with `or` is an error.
- `// comment` runs to the end of the line. `/* comment */` can span lines, but a line break inside it still
  ends the statement it interrupts.
- `///` lines directly above a definition are its documentation. The VS Code extension shows them on hover.

## Randomness and determinism

A world is determined by its story and its seed. The same pair always gives the same history, on the server
and in the browser alike. Every rule draws from its own random stream, keyed by its name, so adding a rule
or reordering rules changes no other rule's draws. Renaming a rule gives it a new stream.

## Where to go next

- [The language reference](language-reference.md), for every built-in and attribute.
- [`MoiraiCli/w.sg`](../MoiraiCli/w.sg), for a whole world: dynasties, wars, faiths, artifacts and ages.
- In VS Code, the extension in `vscode-languageserver/` gives hover documentation, completion and
  go-to-definition. In the browser, the Story page of the [online viewer](https://theor.github.io/Moirai/)
  lets you edit the story and see the engine's errors as you type, with the same hover documentation.
