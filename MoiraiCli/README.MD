TODO
- [ ] `pick?` optional value ?
- [ ] flag enum properties - `flag F { A,B,C }` `set_flag $x.f, F.B` `remove_flag $x.f, F.B` `has_flag $x.f, F.B `?
- [ ] pick $x,$y multiple values at once ? avoid `pick $x...pick $y: $y != $x`
- [ ] parse types props init `type person { alive = true }`
- [ ] restrict props to type ? `type person { alive: number = true }` would allow overlap of prop indices across types and to index them directly
- [ ] assign factor per entity to impact history ?
- [ ] filter sinceLastEvent ? avoid having 4 children in the same year
- [ ] fix shaky implicit variables (equal to -1 or variable.count)

FEATURES
- [ ] curves ?
- [ ] nested property paths. add a ppath nested fieldto ppath ? make a separate NestedPropPath class ?
- [ ] format: prev value ? `set $i.owner = $p;`
  - `format "from prev($i.owner.name) to $p.name" ?`
  - `$prev = $i.owner; $i.owner = $p; format "$prev $p"?`
- [ ] tags ? are they arrays, bitflags, links, their own thing ?
- [ ] arrays ? are links good enough ?
- [x] events description not in history ??
- [x] action tags
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
    Person: 63.60% 3136428 /  4930778
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

no sql, per type cache
500 sampling: findall 1103 ms
500 tracing: findall 12 487 ms 9562 calls

1000: 4.6s, 7525 entities, last: person ascella aureliusson, link, link
3000: 40s, 23091 e, last: Portrait of Silvana Caradoc


sqlite
1000: 5.1s 7525 entities, last: person ascella aureliusson, link, link
3000: 35s, 23091 e, last: Portrait of Silvana Caradoc
500 sampling: findall 1072 ms
500 tracing: findall 1 519 ms 9562 calls

sqlite indexed:
-- CREATE INDEX owners ON entity (owner) WHERE type = 3;
-- CREATE INDEX types ON entity (type,alive)  WHERE type = 2;
1000 2.8s
3000  13s


CREATE INDEX types_alive ON entity (type,alive);
3000 16s


pre-event old/new refactor: 1000 events, 7500ms
after: 8500ms


with 3 events: born, buries children, inherit 7100

1000 events: 4945
cached commands: 4038

SELECT id, rnd() as r FROM entity     LEFT JOIN marked ON marked.eid = entity.id AND marked.marker = 21 WHERE type = 2 AND (alive = 1) AND (age < 4) AND (partner != 0) AND (COALESCE(marked.last_year, 0) > 4) ORDER BY r LIMIT 1
SELECT id, rnd() as r FROM entity     LEFT JOIN marked ON marked.eid = entity.id AND marked.marker = 21 WHERE type = 2 AND (alive = 1) AND (age < 4) AND (partner != 0) AND (COALESCE(765 - marked.last_year, 0) > 4) ORDER BY r LIMIT 1
