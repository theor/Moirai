import {useSelectedEntity} from "./utils.tsx";
import {FamilyTreeNode, useMoiraiStore} from "./SignalRConnection.tsx";
import {useEffect, useRef, useState} from "react";
import * as d3 from "d3";
import * as d3dag from "d3-dag";
import {graphStratify} from "d3-dag";
import {uniqWith} from "lodash";
import {filter} from "d3";
import {colors, Slider} from "@mui/material";
import theme from "./theme.tsx";
// https://localhost:3000/?delta=1000&eid=146&tab=3&f=-1
function arrowTransform({
                            points
                        }: {
    points: readonly (readonly [number, number])[];
}): string {
    const [[x1, y1], [x2, y2]] = points.slice(-2);
    const angle = (Math.atan2(y2 - y1, x2 - x1) * 180) / Math.PI + 90;
    return `translate(${x2}, ${y2}) rotate(${angle})`;
}
export function ChartView(){

    const ref = useRef<SVGSVGElement>(null);
    const [selectedEntity,setSelectedEntity] = useSelectedEntity();
    // const [tree,setTree] = useState<RawNodeDatum>({
    //     name: selectedEntity.toString(),
    // })
    const [treeMaxDepth, setTreeMaxDepth] = useState(3);
    const conn = useMoiraiStore((s) => s.conn);
    if(!conn)
        return <h1>no data</h1>;
   
    useEffect(() => {
     
        const id = selectedEntity;
        console.log("selected", id);
        const svgRoot = d3.select(ref.current!);
        const svgElement = svgRoot.append("g").attr("id", "root");
        conn.getFamilyTree(selectedEntity, treeMaxDepth).then((nodes) => {
            console.log(nodes);
            const builder = graphStratify()
                .id((x:FamilyTreeNode) => x.id.toString())
                .parentIds((x:FamilyTreeNode) => [x.p1.toString(), x.p2.toString()].filter(p => p !== "0"))
            // const d3nodes = stratify(nodes);
            // const d3nodes: TreeNode[] = nodes.map(n => {
            //     const t: TreeNode = {
            //         id: n.id.toString(),
            //         parentIds: [n.p1.toString(), n.p2.toString()].filter(p => p !== "0")
            //     };
            //     return t;
            // });
            //     const map = new Map<number, TreeNode>();
            //     const set = new Set<number>();
            //     nodes.forEach(n => {
            //         map.set(n.id, {name: n.name, children: [], attributes: {id:n.id}});
            //         set.add(n.id);
            //     })
            //     nodes.forEach(n => {
            //         if(n.p1 !== 0) {
            //             const x = map.get(n.p1)!;
            //             if (!x.children)
            //                 x.children = [];
            //             x.children.push(map.get(n.id)!);
            //             map.set(n.p1, x);
            //             set.delete(n.id);
            //         }
            //         if(n.p2 !== 0) {
            //             const x = map.get(n.p2)!;
            //             if (!x.children)
            //                 x.children = [];
            //             x.children.push(map.get(n.id)!);
            //             map.set(n.p2, x);
            //             set.delete(n.id);
            //         }
            //     })
            //     const rootChildren = [];
            //     for (const number of set) {
            //         rootChildren.push(map.get(number)!);
            //     }
            //     // const root: RawNodeDatum = {name: "root", children: rootChildren, attributes: {id: 0, hidden: true}};
            //     // setTree(root)
            //   
            // });
            // },
            //     [selectedEntity]);
            // useEffect(() => {

            const svgDefs = svgElement.append("defs").attr("id", "defs");
            const svgLinks = svgElement.append("g").attr("id", "links");
            const svgHLinks = svgElement.append("g").attr("id", "hlinks");
            const svgNodes = svgElement.append("g").attr("id", "nodes");
            const svgArrows = svgElement.append("g").attr("id", "arrows");
            function handleZoom(e) {
                svgElement
                    .attr('transform', e.transform);
            }

            let zoom = d3.zoom().on('zoom', handleZoom);
            svgRoot.call(zoom);
// create our builder and turn the raw data into a graph
//             const builder = d3dag.graphStratify();
//             console.log(d3nodes);
            const graph = builder(uniqWith(nodes, (a, b) => a.id === b.id));
            // const nodeRadius = 20;
            const nodeWidth = 200;
            const nodeHeight = 40;
            const nodeSize = [nodeWidth, nodeHeight] as const;
// this truncates the edges so we can render arrows nicely
            const shape = d3dag.tweakShape(nodeSize, d3dag.shapeRect);
// use this to render our edges
            const line = d3.line().curve(d3.curveMonotoneY);
            const linestep = d3.line().curve(d3.curveStepBefore);
            const linestep2 = d3.line().curve(d3.curveStepAfter);
// here's the layout operator, uncomment some of the settings
            const layering = d3dag.layeringSimplex();
            // layering.group().
            const layout = d3dag
                .sugiyama()
                .layering(layering)
                .decross(d3dag.decrossTwoLayer())
                // .coord(d3dag.coordGreedy())
                // .coord(d3dag.coordQuad())
                .coord(d3dag.coordSimplex())
                .nodeSize(nodeSize)
                
                .gap([nodeHeight, nodeHeight])
                .tweaks([shape]);

            const {width, height} = layout(graph);
            const steps = graph.nnodes() - 1;
            const interp = d3.interpolateRainbow;
            const colorMap = new Map(
                [...graph.nodes()]
                    .sort((a, b) => a.y - b.y)
                    .map((node, i) => [node.data.id, interp(i / steps)])
            );

// global
            const svg = svgElement
                // pad a little for link thickness
                .style("width", width + 4)
                .style("height", height + 4);
            const trans = svg.transition().duration(750);
            const trans2 = svg.transition().delay(500).duration(750);

            svgNodes.selectAll("g")
                .data(graph.nodes())
                .join((enter) =>
                    enter
                        .append("g")
                        .attr("transform", ({x, y}) => `translate(${x - nodeWidth/2}, ${y - nodeHeight/2})`)
                        .attr("opacity", 0)
                        .call((enter) => {
                            enter
                                .append("rect")
                                .attr("width", nodeWidth)
                                .attr("height", nodeHeight)
                                .attr("fill", (n) => colorMap.get(n.data.id)!)
                            .attr("stroke", n => n.data.id === selectedEntity ? theme.palette.primary.light : "none")
                            .attr("stroke-width", n => n.data.id === selectedEntity ? "4" :  "0");
                            const link = enter.append("a")
                                .attr("href", "#")
                                .on("click", (e,n) => {
                                    setSelectedEntity(n.data.id);
                                    e.preventDefault();
                                });
                            link
                                .append("text")
                                .text((d) => `${d.data.name}`)
                                .attr("x", nodeWidth/2)
                                .attr("y", nodeHeight/2)
                                .attr("font-weight", "bold")
                                .attr("font-family", "sans-serif")
                                .attr("text-anchor", "middle")
                                .attr("alignment-baseline", "middle")
                                .attr("fill", "white");
                            enter.transition(trans).attr("opacity", 1);
                        })
                );

            svgDefs.selectAll("linearGradient")
                .data(graph.links())
                .join((enter) =>
                    enter
                        .append("linearGradient")
                        .attr("id", ({source, target}) =>
                            encodeURIComponent(`${source.data.id}--${target.data.id}`)
                        )
                        .attr("gradientUnits", "userSpaceOnUse")
                        .attr("x1", ({points}) => points[0][0])
                        .attr("x2", ({points}) => points[points.length - 1][0])
                        .attr("y1", ({points}) => points[0][1])
                        .attr("y2", ({points}) => points[points.length - 1][1])
                        .call((enter) => {
                            enter
                                .append("stop")
                                .attr("class", "grad-start")
                                .attr("offset", "0%")
                                .attr("stop-color", ({source}) => colorMap.get(source.data.id)!);
                            enter
                                .append("stop")
                                .attr("class", "grad-stop")
                                .attr("offset", "100%")
                                .attr("stop-color", ({target}) => colorMap.get(target.data.id)!);
                        })
                );
            // svgLinks.selectAll("path")
            //     .data(graph.links())
            //     .join((enter) =>
            //         enter
            //             .append("path")
            //             .attr("d", ({points}) => line(points))
            //             .attr("fill", "none")
            //             .attr("stroke-width", 3)
            //             .attr(
            //                 "stroke",
            //                 ({source, target}) => `url(#${source.data.id}--${target.data.id})`
            //             )
            //             .attr("opacity", 0)
            //             .call((enter) => enter.transition(trans).attr("opacity", 1))
            //     );
            svgHLinks.selectAll(".hlink")
                .data(filter(graph.nodes(), n => n.data.p1 !== 0 && n.data.p2 !== 0))
                .join((enter) => {
                    console.log(enter);
                    return enter.append("path")
                        .attr("class", "hlink")
                        .attr("fill", "none")
                        .attr("stroke-width", 3)
                        .attr("stroke-dasharray", 4)
                        .attr("stroke", "black")
                        .attr("d", (n) => {
                            const [p1,p2] = [...n.parents()];
                            console.log(n.data, n.nparents(), p1,p2);
                            return line([[p1.x, p1.y], [p2.x, p2.y]]);
                        })
                        .attr("opacity", 0)
                        .call((enter) => enter.transition(trans2).attr("opacity", 1));;
                });
            svgHLinks.selectAll(".vlink")
                .data(filter(graph.nodes(), n => n.data.p1 !== 0 && n.data.p2 !== 0))
                .join((enter) => {
                    console.log(enter);
                    return enter.append("path")
                        .attr("class", "vlink")
                        .attr("fill", "none")
                        .attr("stroke-width", 3)
                        .attr("stroke", "black")
                        .attr("d", (n) => {
                            const [p1,p2] = [...n.parents()];
                            console.log(n.data, n.nparents(), p1,p2);
                            const parentsMid:[number,number] = [(p1.x + p2.x) / 2, (p1.y + p2.y) / 2];
                            const parentsYMax = Math.max(p1.y, p2.y);
                            const mid:[number,number] = [(parentsMid[0] + n.x) / 2, (parentsYMax + n.y) / 2];
                            return `${linestep([parentsMid, mid])} ${linestep2([mid, [n.x, n.y]])} `;
                        })
                        .attr("opacity", 0)
                        .call((enter) => enter.transition(trans2).attr("opacity", 1));;
                });
            // svgLinks.selectAll(".vlink")
            //     .data(graph.links())
            //     .join((enter) => {
            //         // console.log(enter)
            //             return enter
            //                 .append("path")
            //                 .attr("class", "vlink")
            //                 .attr("d", ({points}) => line(points))
            //                 .attr("fill", "none")
            //                 .attr("stroke-width", 3)
            //                 .attr(
            //                     "stroke",
            //                     "black"//({source, target}) => `url(#${source.data.id}--${target.data.id})`
            //                 )
            //                 .attr("opacity", 0)
            //                 .call((enter) => enter.transition(trans).attr("opacity", 1));
            //         }
            //     );
            // const arrowSize = 80;
            // const arrowLen = Math.sqrt((4 * arrowSize) / Math.sqrt(3));
            // const arrow = d3.symbol().type(d3.symbolTriangle).size(arrowSize);
            // svgArrows
            //     .selectAll("path")
            //     .data(graph.links())
            //     .join((enter) =>
            //         enter
            //             .append("path")
            //             .attr("d", arrow)
            //             .attr("fill", ({target}) => colorMap.get(target.data.id)!)
            //             .attr("transform", arrowTransform)
            //             .attr("opacity", 0)
            //             .attr("stroke", "white")
            //             .attr("stroke-width", 2)
            //             // use this to put a white boundary on the tip of the arrow
            //             .attr("stroke-dasharray", `${arrowLen},${arrowLen}`)
            //             .call((enter) => enter
            //                 .transition(trans)
            //                 .attr("opacity", 1))
            //     );

        });
        return () => {
            svgElement.selectAll("*").remove();
        }
    }, [selectedEntity, treeMaxDepth])
    return <div style={{height:"100%", overflowY:"auto"}}>
        <h1>Family tree</h1>
        <Slider sx={{maxWidth: "20vw"}} valueLabelDisplay="auto" value={treeMaxDepth} onChange={(_,v) => setTreeMaxDepth(v as number)}/>
        <div id="treeWrapper" style={{ width: '100%', height: '100%' }}>
            <svg ref={ref} style={{ width: '100%', height: '100%' }}>
               
            </svg>
            {/*<Tree pathFunc={"step"} orientation={"vertical"} data={tree} />*/}
        </div>
    </div>
}
