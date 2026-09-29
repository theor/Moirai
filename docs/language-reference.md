# Moirai language reference

<!-- Generated from language-reference.json by Moirai.Tests/LanguageReferenceTests.cs. Do not edit: change the documentation in Moirai.Parser/StoryParser.cs, then run `UPDATE_GOLDENS=1 dotnet test Moirai.Tests`. -->

Every built-in function and attribute of the `.sg` language. For how a story is put together, read the [language guide](language.md) first.

- [Queries](#queries)
- [Entities](#entities)
- [Rules and scheduling](#rules-and-scheduling)
- [Randomness](#randomness)
- [Records](#records)
- [Kinship](#kinship)
- [Collections](#collections)
- [Math and logic](#math-and-logic)
- [Testing and debugging](#testing-and-debugging)
- [Event and trigger attributes](#event-and-trigger-attributes)
- [Type attributes](#type-attributes)

## Queries

### `avg`

```moirai
avg T $v: ([predicate..., ]value: number): number
```

The mean of `value` over every T the predicate matches, 0 when none does: use `count` to tell none from zero. The mean of whole numbers is a float. Written like `sum`.

```moirai
var $mean: avg Person $p: (alive, $p.happiness)
```

### `count`

```moirai
count T $v[: (predicate...)]: number
count(coll: collection): number
```

`count T $v: (predicate)` is how many T the predicate matches; with no predicate, how many T exist at all. $v exists only inside the call. `count(coll)` is the number of values in a collection property. Draws no random numbers, and can sit inside another query's predicate.

```moirai
var $living: count Person $p: (alive)
pick Person $p: (alive, count($p.friends) < 3)
```

### `each`

```moirai
each T $v[: (predicate...)] { ... }
```

Runs the block once for every T the predicate matches, with $v bound to each in turn, oldest entity first; with no predicate, for every T. The matches are found before the block first runs, so entities it creates are not visited. A statement in the block that stops (a failed pick) ends only that iteration. `each` never fails, even when nothing matches.

- `{ ... }`: runs once per match; sees the rule's locals and $v.

```moirai
each Person $p: (alive, age > 60) {
    set $p.wealth = $p.wealth + 1
}
```

### `max`

```moirai
max T $v: ([predicate..., ]value: number): number
```

The largest `value` over every T the predicate matches, 0 when none does. Written like `sum`.

```moirai
var $oldest: max Person $p: (alive, $p.age)
```

### `min`

```moirai
min T $v: ([predicate..., ]value: number): number
```

The smallest `value` over every T the predicate matches, 0 when none does. Written like `sum`.

```moirai
var $youngest: min Person $p: (alive, $p.age)
```

### `pick`

```moirai
pick T $v[: (predicate...)]
pick T $v[: (predicate...)] else { ... }
```

Picks one T the predicate matches, uniformly at random, and binds it to $v; with no predicate, any T. When nothing matches, the rule stops there and has failed: what it already did stays in the world, but its changes are not logged and no trigger sees them, so pick before changing anything. With `else`, the block runs instead and the rule then stops *successfully*: its changes are logged and triggers see them. As a condition, `if (pick T $v: (...)) { }` tests whether anything matched.

- `else { ... }`: runs when nothing matches, then the rule stops successfully; sees the rule's locals but not $v.

```moirai
pick Person $p: (alive, partner = null) else {
    record('Nobody is left to marry')
}
record('{$p.name} stays single')
```

### `sum`

```moirai
sum T $v: ([predicate..., ]value: number): number
```

The total of `value` over every T the predicate matches, 0 when none does. The arguments before the last are the predicate, joined by `and` like a pick's. $v exists only inside the call. A sum of percentages is a float, since it can pass 100. Draws no random numbers of its own, and can sit inside another query's predicate.

```moirai
var $total: sum Person $p: (alive, $p.wealth)
```

## Entities

### `create`

```moirai
create T $v
create T $v { prop := value ... }
create T $v: 'name'
create T $v: 'name' { prop := value ... }
```

Creates a new T and binds it to $v, which stays in scope for the rest of the rule. The name is an interpolated string. The block sets properties on the new entity, one `prop := value` per line, before anything else sees it. Creating a singleton that already exists binds the existing one instead of making a second.

- `{ ... }`: sets properties on the new entity; sees the rule's locals and $v.

```moirai
create Person $p: '{roll(Name)}' {
    age := 0
    alive := true
}
```

## Rules and scheduling

### `mark`

```moirai
mark(entity: entity)
```

Remembers that this rule touched `entity` this year, for `since_last` to read. Marks belong to the rule that makes them, so pair `mark` and `since_last` in the same rule, which must be an event or a trigger. Use the pair to keep a rule from picking the same entity again too soon.

```moirai
pick Person $p: (alive, since_last($p) > 4)
mark($p)
record('{$p.name} goes on a pilgrimage')
```

### `repeat`

```moirai
repeat(n: number) { ... }
```

Runs the block n times; n is read once, before the first turn. Each turn is a scope of its own, and a statement that stops (a failed pick) ends that turn only, so calling an event n times this way is n separate runs of it. Draws no random numbers of its own.

- `{ ... }`: runs n times; a stop ends that turn only; sees the rule's locals.

```moirai
repeat(3) {
    harvest()
}
repeat(count Person $p: (alive)) {
    record('A lantern is lit')
}
```

### `schedule`

```moirai
schedule(entity: entity, year: number) { ... }
```

Defers the block until the simulation reaches `year`, then runs it once, as a rule of its own, with $self bound to `entity`. Both arguments are evaluated now. The block sees $self but none of the enclosing rule's locals, so read what it needs from $self. A year that is not in the future fires next year. If the entity no longer exists by then, nothing runs, so a block that cares whether $self is still alive should test it.

- `{ ... }`: runs in a later year, as a rule of its own; sees only $self.

```moirai
pick Person $p: (alive)
schedule($p, #Time.year + 20) {
    if $self.alive {
        set $self.age = $self.age + 20
    }
}
```

### `since_last`

```moirai
since_last(entity: entity): number
```

The number of years since this rule last called `mark(entity)`. An entity this rule never marked counts as marked in year 0, so it reads as long ago rather than as a special value. Usable in a pick's predicate.

```moirai
pick Person $p: (alive, since_last($p) > 10)
mark($p)
```

## Randomness

### `chance`

```moirai
chance(p: number): bool
chance(p: number) { ... }
```

True with probability p, a percentage: `chance(3%)`. It always draws exactly once, whatever p is. With a block, it is a statement: the block runs when the draw hits, and the rule carries on either way. It cannot be part of a pick or each predicate: pick first, then test chance.

- `{ ... }`: runs when the draw hits; sees the rule's locals.

```moirai
pick Person $p: (alive)
chance(5%) {
    record('{$p.name} finds a fortune')
}
if chance(50%) {
    set $p.wealth = $p.wealth + 1
}
```

### `random`

```moirai
random(E: enum): E
random(max: number literal): number
random(min: number literal, max: number): number
```

`random(E)` is one of the enum's values, each equally likely. `random(max)` is a whole number from 0 to max - 1, and `random(min, max)` one from min to max - 1: the upper bound is never drawn. When max is not above min, the result is min. Every call draws from the rule's own random stream, so the world stays the same for a given seed.

```moirai
pick Person $x: (alive)
set $x.job = random(Job)
if random(100) < 8 {
    set $x.wealth = random(40, 90)
}
```

### `roll`

```moirai
roll(T: table): entry of T
```

Draws one entry from a `table`, according to its weights.

```moirai
create Person $p: '{roll(Name)}'
```

## Records

### `link`

```moirai
link(entity: entity, text: string): string
```

Inside a record's text, shows `text` as a link to `entity`. Use it when the words should not be the entity's name. Unlike `{$p.name}`, a link does not make the entity one of the record's participants.

```moirai
pick Person $p: (alive)
record('{$p.name} paints a {link($p, 'self-portrait')}')
```

### `record`

```moirai
record('text'[, weight: number])
```

Writes a sentence into the world's history. The text is interpolated: `{$p.name}` inserts a value, and an entity mentioned this way becomes a participant of the record, so it links to that entity and shows on its Life page. The optional weight (taken as a whole number) says how much the record matters: 1 by default, 0 for background noise, higher for the turning points the chronicle surfaces. The weight is metadata only and never changes how the world runs.

```moirai
pick Person $p: (alive)
record('{$p.name} is crowned', 5)
```

## Kinship

### `related`

```moirai
related(a: entity, b: entity, n: 0..6): bool
```

True when a and b share an ancestor within n degrees of kinship, counted the civil-law way: parent 1, grandparent or sibling 2, aunt or uncle 3, first cousin 4. The parents are the type's `@parents`, or `parent1`/`parent2` by default, and the type must have them.

```moirai
pick Person $x: (alive, partner = null)
pick Person $y: (alive, partner = null, $y != $x, not(related($x, $y, 4)))
```

## Collections

### `add`

```moirai
add(coll: collection, value: element of coll)
```

Adds a value to a collection property (`prop friends: [Person]`). A collection is a set: adding a value it already holds changes nothing.

```moirai
pick Person $a: (alive)
pick Person $b: (alive, $b != $a)
add($a.friends, $b)
```

### `contains`

```moirai
contains(coll: collection, value: element of coll): bool
```

True when the collection property holds the value. Usable in a pick's predicate.

```moirai
pick Person $a: (alive)
pick Person $b: (alive, $b != $a, not(contains($a.friends, $b)))
```

### `remove`

```moirai
remove(coll: collection, value: element of coll)
```

Removes a value from a collection property. Removing a value it does not hold changes nothing.

```moirai
pick Person $a: (alive)
pick Person $b: (contains($a.friends, $b))
remove($a.friends, $b)
```

## Math and logic

### `ceiling`

```moirai
ceiling(x: number): number
```

x rounded up to a whole number.

```moirai
var $boats: ceiling(count Person $p: (alive) / 12)
```

### `clamp01`

```moirai
clamp01(x: number): number
```

x limited to the range 0 to 1. It is for fractions: a percentage is held as 0 to 100 and is already kept in that range whenever it is set.

```moirai
var $share: clamp01(count Person $p: (alive) / 1000)
```

### `floor`

```moirai
floor(x: number): number
```

x rounded down to a whole number.

```moirai
var $half: floor(count Person $p: (alive) / 2)
```

### `not`

```moirai
not(condition: condition): bool
```

True when the condition is false. Usable in a pick's predicate.

```moirai
pick Person $p: (not($p.alive))
```

### `round`

```moirai
round(x: number): number
```

x rounded to the nearest whole number. A half rounds to the even neighbour: round(2.5) is 2, round(3.5) is 4.

```moirai
var $mean: round(avg Person $p: (alive, $p.age))
```

## Testing and debugging

### `assert`

```moirai
assert(condition: condition)
```

Stops the simulation with an error when the condition is false. The error quotes the condition as written.

```moirai
pick Person $p: (alive)
assert($p.age >= 0)
```

### `assert_eq`

```moirai
assert_eq(actual: any, expected: any)
```

Stops the simulation with an error when the two values differ. The error quotes both expressions as written and prints both values.

```moirai
create Person $p: 'Ada' {
    age := 3
}
assert_eq($p.age, 3)
```

### `debug`

```moirai
debug(value: any, ...)
```

Prints each argument, as written and as it evaluates, to the host's console (the server's, or the browser's developer console). It changes nothing in the world.

```moirai
pick Person $p: (alive)
debug($p.name, $p.age)
```

## Event and trigger attributes

### `@frequency`

```moirai
@frequency(x, PerXYear | EveryXYear, y)
```

Schedules the event. `PerXYear` is random: on average x runs every y years, drawn each year, so a year can have none or several. `EveryXYear` is exact: x runs in every window of y years, each on a year of the window drawn at random. An event with no scheduling attribute runs only when something calls it.

- `x`: a whole-number literal, at least 1.
- `mode`: one of `PerXYear`, `EveryXYear`.
- `y`: a whole-number literal, at least 1.

Applies to: events.

```moirai
@frequency(1, PerXYear, 2)
event quarrel {
    record('A quarrel')
}
@frequency(1, EveryXYear, 10)
event census {
    record('A census')
}
```

### `@start`

```moirai
@start
```

Runs the event once, when the world is created, before the first year passes. This is where a story sets the clock (`set #Time.year = 764`) and makes its first entities. Several `@start` events run in the order they are written.

Applies to: events.

```moirai
@start
event founding {
    create Person $p: 'Ada' {
        alive := true
    }
}
```

### `@tag`

```moirai
@tag('name', ...)
```

Labels an event or trigger with one or more names. The viewer's Records page groups and filters records by them.

- `name`: a string literal (one or more).

Applies to: events, triggers.

```moirai
@tag('family', 'noise')
@frequency(1, PerXYear, 1)
event chatter {
    record('People talk', 0)
}
```

## Type attributes

### `@alive`

```moirai
@alive(p)
```

Names the property that is true while an entity lives. The viewer counts the living with it. Defaults to `alive`. A type takes `@alive` or `@dead`, not both.

- `p`: a bool property of the annotated type.

Applies to: types.

```moirai
@alive(breathing)
entity Kin {
    prop breathing: bool
}
```

### `@born`

```moirai
@born(p)
```

Names the property that holds the year an entity was born. Defaults to `birthdate`, and then to the year the entity was created.

- `p`: a number property of the annotated type.

Applies to: types.

```moirai
@born(born_in)
entity Kin {
    prop born_in: number
}
```

### `@dead`

```moirai
@dead(p)
```

Names a property that is true once an entity has died, for a story that tracks death rather than life. A type takes `@alive` or `@dead`, not both.

- `p`: a bool property of the annotated type.

Applies to: types.

```moirai
@dead(fallen)
entity Kin {
    prop fallen: bool
}
```

### `@died`

```moirai
@died(p)
```

Names the property that holds the year an entity died. Defaults to `deathdate`.

- `p`: a number property of the annotated type.

Applies to: types.

```moirai
@died(died_in)
entity Kin {
    prop died_in: number
}
```

### `@display`

```moirai
@display(OtherType, 'Label', predicate[, 'item format'])
```

Adds a derived field to the entity's details in the viewer: every OtherType the predicate matches, listed under Label, each written with the item format when there is one.

- `OtherType`: an entity type.
- `Label`: a string literal.
- `predicate`: a predicate over the `OtherType` entities: `$self` is the annotated entity, `$other` the candidate, and a bare property name reads `$other`'s.
- `item format`: a string literal, which can interpolate `$other` (optional).

Applies to: types.

```moirai
@display(Kin, 'Children', parent1 = $self or parent2 = $self)
@display(Kin, 'Friends', contains($self.friends, $other), '{$other.name}, aged {$other.age}')
entity Kin {
    prop age: number
    prop parent1: Kin
    prop parent2: Kin
    prop friends: [Kin]
}
```

### `@parents`

```moirai
@parents(a, b)
```

Names the two properties that hold an entity's parents. `related()`, the family tree and the Life page read them. Without it, properties named `parent1` and `parent2` are used.

- `a`: a property of the annotated type that refers to that same type.
- `b`: a property of the annotated type that refers to that same type.

Applies to: types.

```moirai
@parents(mother, father)
entity Kin {
    prop mother: Kin
    prop father: Kin
}
```

### `@partner`

```moirai
@partner(p)
```

Names the property that holds an entity's partner, shown in the family tree. Defaults to a property named `partner`.

- `p`: a property of the annotated type that refers to that same type.

Applies to: types.

```moirai
@partner(spouse)
entity Kin {
    prop spouse: Kin
}
```

### `@period`

```moirai
@period(from, to)
```

Marks the type as an age of history, from its first year to its last. The chronicle on the Home page and the exported history are chaptered by these. Without it, a type with `start_year` and `end_year` and no reference properties counts as one.

- `from`: a number property of the annotated type.
- `to`: a number property of the annotated type.

Applies to: types.

```moirai
@period(from, to)
entity Era {
    prop from: number
    prop to: number
}
```

### `@population`

```moirai
@population
```

Marks the type whose living count is the world's population, plotted on the Home page. At most one type can carry it. Without it, the largest type with an alive or dead role is used.

Applies to: types.

```moirai
@population
entity Villager {
    prop alive: bool
}
```
