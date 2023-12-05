import {useMoiraiStore} from "./SignalRConnection.tsx";
import {useCallback, useEffect} from "react";
import {Changeset} from "./types.ts";
import {TableComponents, TableVirtuoso} from "react-virtuoso";
import * as React from "react";
import TableContainer from "@mui/material/TableContainer";
import Paper from "@mui/material/Paper";
import Table from "@mui/material/Table";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";

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

const RecordTable: TableComponents<Changeset> = {
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
    dataKey: keyof Changeset;
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
        width: 60,
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

function rowContent(_index: number, row: Changeset) {
    // const text = makeEntityLink(row.text, selectedEntity, filteredEntity);
    return (
        <React.Fragment>
            {columns.map((column) => (
                <TableCell
                    key={column.dataKey}
                    align={column.numeric || false ? 'right' : 'left'}
                >

                    {row ? (column.dataKey === "changes" ? (row['changes']?.length ?? 0) :
                         row[column.dataKey]) : "_"
                    }
                </TableCell>
            ))}
        </React.Fragment>
    );
}

export function ChangesetList() {
    const conn = useMoiraiStore(s => s.conn);
    const changesets = useMoiraiStore(s => s.changesets);
    const addChangesets = useMoiraiStore(s => s.addChangesets);
    
    const loadMore = useCallback(() => setTimeout(() => {
        console.log("load more", changesets.length)
        conn?.getChangesets(changesets.length, 20).then(([c,x]) => {
            console.log(c,x); addChangesets(x);})
    }, 200), [changesets]);
    
    useEffect(() => {
        console.log("EFFECT")
         conn?.getChangesets(0, 20).then(([c,x]) => {
             let a = Array(c + x.length).fill(null);
             a.splice(0, 0, ...x);
             console.log(c,x, a); addChangesets(a);})
    }, []);
    // const table = useReactTable<Changeset>({
    //     data:changesets,
    //     columns,
    //     getCoreRowModel: getCoreRowModel(),
    // });
    return  <TableVirtuoso
        data={changesets}
        // endReached={loadMore}
        overscan={200}
        // totalCount={1000}
        increaseViewportBy={{ top: 800, bottom: 300 }}
        components={RecordTable}
        fixedHeaderContent={fixedHeaderContent}
        itemContent={rowContent}
    />;
}
