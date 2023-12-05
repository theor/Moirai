import {useMoiraiStore} from "./SignalRConnection.tsx";
import {useEffect} from "react";
import {createColumnHelper, flexRender, getCoreRowModel, useReactTable} from "@tanstack/react-table";
import {Changeset} from "./types.ts";

const columnHelper = createColumnHelper<Changeset>();
const columns = [
    columnHelper.accessor(x => x.id, {
        id:'id',
    }),
    columnHelper.accessor(x => x.year, {
        id:'year',
    }),
    columnHelper.accessor(x => x.actionName, {
        id:'actionName',
    }),
];
export function ChangesetList() {
    const conn = useMoiraiStore(s => s.conn);
    const changesets = useMoiraiStore(s => s.changesets);
    const addChangesets = useMoiraiStore(s => s.addChangesets);
    
    useEffect(() => {
         conn?.getChangesets(0, 20).then(([c,x]) => {
             console.log(c,x); addChangesets(x);})
    }, []);
    const table = useReactTable<Changeset>({
        data:changesets,
        columns,
        getCoreRowModel: getCoreRowModel(),
    });
    return   <table>
        <thead>
        {table.getHeaderGroups().map(headerGroup => (
            <tr key={headerGroup.id}>
                {headerGroup.headers.map(header => (
                    <th key={header.id}>
                        {header.isPlaceholder
                            ? null
                            : flexRender(
                                header.column.columnDef.header,
                                header.getContext()
                            )}
                    </th>
                ))}
            </tr>
        ))}
        </thead>
        <tbody>
        {table.getRowModel().rows.map(row => (
            <tr key={row.id}>
                {row.getVisibleCells().map(cell => (
                    <td key={cell.id}>
                        {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </td>
                ))}
            </tr>
        ))}
        </tbody>
        <tfoot>
        {table.getFooterGroups().map(footerGroup => (
            <tr key={footerGroup.id}>
                {footerGroup.headers.map(header => (
                    <th key={header.id}>
                        {header.isPlaceholder
                            ? null
                            : flexRender(
                                header.column.columnDef.footer,
                                header.getContext()
                            )}
                    </th>
                ))}
            </tr>
        ))}
        </tfoot>
    </table>
}
