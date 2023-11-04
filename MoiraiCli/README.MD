TODO
- [ ] singletons ? `#Time.year`
- [ ] pick $x,$y multiple values at once ? avoid `pick $x...pick $y: $y != $x`
- [x] parse types ? either an enum or `type person { alive, age, job }
  - [ ] parse types props init `type person { alive = true }`
- [ ] probabilities, curves ? assign proba per entity to impact history ?

FEATURES
- [ ] time passing
- [ ] format: prev value ? `set $i.owner = $p;`
  - `format "from prev($i.owner.name) to $p.name" ?`
  - `$prev = $i.owner; $i.owner = $p; format "$prev $p"?`
- [ ] tags ?
- [ ] arrays ? are links good enough ?
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