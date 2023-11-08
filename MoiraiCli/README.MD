TODO
- [ ] pick $x,$y multiple values at once ? avoid `pick $x...pick $y: $y != $x`
- [ ] parse types props init `type person { alive = true }`
- [ ] restrict props to type ? `type person { alive: number = true }` would allow overlap of prop indices across types and to index them directly
- [ ] assign factor per entity to impact history ?

FEATURES
- [ ] curves ?
- [ ] nested property paths. add a ppath nested fieldto ppath ? make a separate NestedPropPath class ?
- [ ] format: prev value ? `set $i.owner = $p;`
  - `format "from prev($i.owner.name) to $p.name" ?`
  - `$prev = $i.owner; $i.owner = $p; format "$prev $p"?`
- [ ] tags ? are they arrays, bitflags, links, their own thing ?
- [ ] arrays ? are links good enough ?
- [x] time passing
- [x] parse types ? either an enum or `type person { alive, age, job }
- [x] probabilities ?
- [x] singletons ? `#Time.year`
- [x] links
  - [ ] specific syntax ??
- [x] parse prop type and use it in printer, maybe in operators ?
- [x] parse enums `enum job { farmer, smith }`, `set job = random(job)`
- [x] clean variant value
- [x] format history
- [x] keep diff during action run
- [x] parsing:
  - [x] `pick $i: type=item, owner != null` ?
  - [x] `$i = pick(type=item, owner != null)`
  - [x] `$p = pick(type=person, id != $i.owner) <- $i.owner doesn't work`
- [x] pick must be random
- [x] wip add factions ?
- [x] fix single parent

run 100 years twice:
- init 147, 365 ms
- type prop: 128, 350
- 1000 twice: 4800ms, 13 300ms
- 3000: 42s
prealloc all properties for each entity:
- 1000 twice: 4600ms, 12 600ms
- 3000: 37s

hit on 500y:
    Person: 63.60% 3136428 / 4930778
      Item: 13.68% 433517  / 3166992
   Faction:  2.92% 3860    / 131889
      Link: 10.25% 769908  / 7507848
   Country:  7.20% 6980    / 96881
       God:  2.46% 2370    / 96254
per type index
- 1000 twice: 2200, 5100
- 3000: 17s
    Person: 100.00% 3136428 / 3136428
      Item:  99.82% 433517 / 434268
   Faction:  93.10% 3860 / 4146
      Link:  99.76% 769908 / 771700
   Country:  79.76% 6980 / 8751
       God:  67.44% 2370 / 3514

op equality ??