TODO
- [ ] pick $x,$y multiple values at once ? avoid `pick $x...pick $y: $y != $x`
- [ ] parse types props init `type person { alive = true }`
- [ ] restrict props to type ? `type person { alive: number = true }`
- [ ] curves ?
- [ ] assign factor per entity to impact history ?
  - `rule x @start {}`
  - `rule x @every 15 years`
  - `rule x @random 1,4 per 2 years`
```
for i in 0..years {
  distrib expected = rule.distribution
  distrib actual = compute(rule)
  if(actual < expected) run(rule){}
  
  1 every 15 years =
  1/15 per year
  
  
  time since last: >=15 -> p = 1
  time since last: 0 -> p = 1/15
}
```

FEATURES
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