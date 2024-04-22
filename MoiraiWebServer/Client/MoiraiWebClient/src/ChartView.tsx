import {useSelectedEntity} from "./utils.tsx";
import {useMoiraiStore} from "./SignalRConnection.tsx";
import {Component, useEffect, useRef} from "react";
import * as React from "react";
import FamilyTree from "@balkangraph/familytree.js";

interface TreeProps {
    nodes?: any[];
start: number;
}
interface TreeState {
    current: number;
}
export default class Chart extends Component<TreeProps, TreeState> {
    private divRef: React.RefObject<HTMLDivElement>;
    family: FamilyTree|undefined;

    constructor(props:any) {
        super(props);
        this.divRef = React.createRef();
        // FamilyTree.templates.hugo.size = [120,120];
    }
    
    public reset() {
        console.log("reset")
        if(this.family && this.divRef.current) {
            try {
                this.family.destroy();
                
            }
            catch (e) {
                
            }
        }
        this.componentDidMount();
    }

    shouldComponentUpdate(nextProps: TreeProps) {
        // console.log(this.props, nextProps);
        // if(this.props.start !== nextProps.start)
        // {
        //     this.family?.destroy();
        //     this.componentDidMount();
        // }
        return false;
    }

    componentDidMount() {
        this.family = new FamilyTree (this.divRef.current! , {
            nodes: this.props.nodes ?? [],
            template: "hugo",

            nodeBinding: {
                field_0: 'name',
            }
        });
    }

    render() {
        return (
            <div style={{height: "100%"}} id="tree" ref={this.divRef}></div>
        );
    }
}
export function ChartView(){
    const chart = React.createRef<Chart>();
    const [selectedEntity,_] = useSelectedEntity();
    const conn = useMoiraiStore((s) => s.conn);
    if(!conn)
        return <h1>no data</h1>;
    // useEffect(() => {
    //     if(!chart.current?.family)
    //         return;
        // console.log("adding node", selectedEntity, chart.current.props.start)
        
        
    // }, [selectedEntity]);
    useEffect(() => {
        console.log(chart.current);
        if(chart.current)
            chart.current?.reset();
        else
            return;
        const id = selectedEntity;
        console.log("selected", id);
        conn.getFamilyTree(selectedEntity).then((nodes) => {
            console.log(nodes);
            nodes.forEach(n => {
                chart.current?.family?.addNode({
                    id: n.id,
                    name: n.name,
                });
            });
            nodes.forEach(n => {
                chart.current?.family?.updateNode({
                    id: n.id,

                    mid: n.p1 == 0 ? undefined : n.p1,
                    fid: n.p2 == 0 ? undefined : n.p2,
                });
            });
                   
        });
    },[selectedEntity, chart]);
    
    return <div style={{height:"100%", overflowY:"auto"}}>
        <h1>chart {selectedEntity}</h1>
        <Chart ref={chart} start={selectedEntity} />
    {/*    nodes={[*/}
    {/*    { id: 1, pids: [2], gender:"female", name: 'Amber McKenzie',  img: 'https://cdn.balkan.app/shared/2.jpg'  },*/}
    {/*    { id: 2, pids: [1], gender:"female", name: 'Ava Field',  img: 'https://cdn.balkan.app/shared/m30/5.jpg' },*/}
    {/*    { id: 3, mid: 1, fid: 2, gender:"female", name: 'Peter Stevens',  img: 'https://cdn.balkan.app/shared/m10/2.jpg' },*/}
    {/*    { id: 4, mid: 1, fid: 2, gender:"female", name: 'Savin Stevens',  img: 'https://cdn.balkan.app/shared/m10/1.jpg'  },*/}
    {/*    { id: 5, mid: 1, fid: 2, gender:"female", name: 'Emma Stevens',  img: 'https://cdn.balkan.app/shared/w10/3.jpg' }*/}
    {/*]}*/}
        {/*<pre >{JSON.stringify(clientData, null, 2)}</pre>*/}
    </div>
}
