import {EntityChangeDisplay, useMoiraiStore} from "./SignalRConnection.tsx";
import {useEffect} from "react";
import {Changeset, GetSetProperty} from "./types.ts";
import {TableComponents, TableVirtuoso} from "react-virtuoso";
import * as React from "react";
import TableContainer from "@mui/material/TableContainer";
import Paper from "@mui/material/Paper";
import Table from "@mui/material/Table";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import {Simulate} from "react-dom/test-utils";
import {makeEntityChip, makeEntityLink, useFiltering, useSelectedEntity} from "./utils.tsx";

// const columnHelper = createColumnHelper<Changeset>();
// const columns = [
//     columnHelper.accessor(x => x.id, {
//         id:'id',
//     }),
//     columnHelper.accessor(x => x.year, {
//         id:'year',
//     }),
//     columnHelper.accessor(x => x.actionName, {
//         id:'actionName',
//     }),
// ];

const RecordTable: TableComponents<EntityChangeDisplay, [GetSetProperty<number>, GetSetProperty<number>]> = {
    Scroller: React.forwardRef<HTMLDivElement>((props, ref) => (
        <TableContainer  component={Paper} {...props} ref={ref}/>
    )),
    Table: (props) => (
        <Table size="small" {...props} sx={{height: '100%', borderCollapse: 'separate', tableLayout: 'fixed'}}/>
    ),
    TableHead,
    TableRow: ({item: _item, ...props}) => <TableRow {...props} />,
    TableBody: React.forwardRef<HTMLTableSectionElement>((props, ref) => (
        <TableBody {...props} ref={ref}/>
    )),
};

interface ColumnData {
    dataKey: keyof EntityChangeDisplay;
    label: string;
    numeric?: boolean;
    width?: number;
}
const columns: ColumnData[] = [
    // {
    //     width: 200,
    //     label: 'Id',
    //     dataKey: 'id',
    // },
    {
        width: 100,
        label: 'Id',
        dataKey: 'id',
    },
    {
        width: 60,
        label: 'Year',
        dataKey: 'year',
        numeric: true,
    },
    {
        label: 'Action',
        dataKey: 'actionName',
    },
    {
        label:'Change count',
        dataKey: 'changes',
    },

];

function fixedHeaderContent() {
    return (
        <TableRow>
            {columns.map((column) => (
                <TableCell
                    key={column.dataKey}
                    variant="head"
                    align={column.numeric || false ? 'right' : 'left'}
                    style={{width: column.width ?? "auto"}}
                    sx={{
                        backgroundColor: 'background.paper',
                    }}
                >
                    {column.label}
                </TableCell>
            ))}
        </TableRow>
    );
}

export function ChangesetList() {

    // function change(c:Changed) {
    //     if(c.prev.id === 0)
    //         return <span>NEW {c.new.properties.filter(p => p.id !== 0).map(p => <span>{JSON.stringify(p)}</span>)}</span>
    //         return <span>SET {c.new.properties.filter(p => p.id !== 0).map(p => <span>{JSON.stringify(p)}</span>)}</span>
    // }
    function rowContent(index: number, row: EntityChangeDisplay, [selectedEntity, filteredEntity]: [GetSetProperty<number>,GetSetProperty<number>]) {
        // const text = makeEntityLink(row.text, selectedEntity, filteredEntity);
       
        return (
            <React.Fragment key={index}>
                {columns.map((column) => {
                    let content: JSX.Element;
                    if (row) {
                        if (column.dataKey === "id") {
                            content = makeEntityChip(row["id"], '#'+row["id"].toString(), selectedEntity, filteredEntity);
                        } else
                        if (column.dataKey === "changes") {
                            content = <ul>{row["changes"]?.map((c,i) => <li key={i}><b>{c.label}</b>: {makeEntityLink(c.value, selectedEntity, filteredEntity)}</li>)}</ul>
                            // content = row['changes']?.length ?? 0;
                        } else {
                            content = <>{row[column.dataKey]}</>;
                        }
                    } else {
                        content = <>{"_"}</>;
                    }
                    return (
                        <TableCell
                            key={column.dataKey}
                            align={column.numeric || false ? 'right' : 'left'}
                        >
                            {content}
                        </TableCell>
                    );
                })}
            </React.Fragment>
        );
    }
    const conn = useMoiraiStore(s => s.conn!);
    const changesets = useMoiraiStore(s => s.changesets);
    const pushChangesets = useMoiraiStore(s => s.pushChangesets);
    // const addChangesets = useMoiraiStore(s => s.addChangesets);
    const selectedEntity = useSelectedEntity();
    const [filteredEntity, setFilteredEntity] = useFiltering();
    const filteredChangesets = changesets.filter(r => (filteredEntity === -1 || isNaN(filteredEntity) || r.id === filteredEntity));
    useEffect(() => {
        console.log("CS")
        pushChangesets([]);
        let buffer: EntityChangeDisplay[] = [];
        setInterval(() => {
            if (buffer.length > 0) {
                pushChangesets([...changesets, ...buffer]);
                buffer = [];
            }
        }, 500);
        const stream = conn?.getChangesets().subscribe({
            next(value: EntityChangeDisplay) {
              
                        buffer.push(value);
                  
            },
            error(err: any) {
                console.error(err)
            },
            complete() {
            }
        })
        return () => stream?.dispose();
    }, []);
    // const table = useReactTable<Changeset>({
    //     data:changesets,
    //     columns,
    //     getCoreRowModel: getCoreRowModel(),
    // });
    return  <TableVirtuoso
        context={[selectedEntity, [filteredEntity, setFilteredEntity]]}
        data={filteredChangesets}
        // endReached={loadMore}
        // overscan={200}
        // rangeChanged={rangeChanged}
        // increaseViewportBy={{ top: 800, bottom: 300 }}
        components={RecordTable}
        fixedHeaderContent={fixedHeaderContent}
        itemContent={rowContent}
    />;
}
