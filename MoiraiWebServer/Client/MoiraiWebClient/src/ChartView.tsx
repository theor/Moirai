import {useSelectedEntity} from "./utils.tsx";
import {useMoiraiStore} from "./SignalRConnection.tsx";
import {useEffect, useState} from "react";
import Tree, {RawNodeDatum} from 'react-d3-tree';
export function ChartView(){
    const [selectedEntity,_] = useSelectedEntity();
    const [tree,setTree] = useState<RawNodeDatum>({
        name: selectedEntity.toString(),
    })
    const conn = useMoiraiStore((s) => s.conn);
    if(!conn)
        return <h1>no data</h1>;
   
    useEffect(() => {
     
        const id = selectedEntity;
        console.log("selected", id);
        conn.getFamilyTree(selectedEntity).then((nodes) => {
            console.log(nodes);
            var map = new Map<number, RawNodeDatum>();
            var set = new Set<number>();
            nodes.forEach(n => {
                map.set(n.id, {name: n.name, children: [], attributes: {id:n.id}});
                set.add(n.id);
            })
            nodes.forEach(n => {
                if(n.p1 !== 0) {
                    const x = map.get(n.p1)!;
                    if (!x.children)
                        x.children = [];
                    x.children.push(map.get(n.id)!);
                    map.set(n.p1, x);
                    set.delete(n.id);
                }
                if(n.p2 !== 0) {
                    const x = map.get(n.p2)!;
                    if (!x.children)
                        x.children = [];
                    x.children.push(map.get(n.id)!);
                    map.set(n.p2, x);
                    set.delete(n.id);
                }
            })
            const rootChildren = [];
            for (const number of set) {
                rootChildren.push(map.get(number)!);
            }
            const root: RawNodeDatum = {name: "root", children: rootChildren, attributes: {id: 0, hidden: true}};
            setTree(root)
           
        });
    },[selectedEntity]);
    
    return <div style={{height:"100%", overflowY:"auto"}}>
        <h1>chart {selectedEntity}</h1>
        <div id="treeWrapper" style={{ width: '100%', height: '100%' }}>
            <Tree pathFunc={"step"} orientation={"vertical"} data={tree} />
        </div>
    </div>
}
