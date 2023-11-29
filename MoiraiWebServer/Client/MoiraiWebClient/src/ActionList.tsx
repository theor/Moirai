import {
    Divider,
    Switch,
    TableRow,
    Typography
} from "@mui/material";
import {useContext} from "react";
import {SignalRConnectionContext} from "./SignalRConnection.tsx";
import Table from "@mui/material/Table";
import TableContainer from "@mui/material/TableContainer";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import {ActionData} from "./types.ts";

export function ActionList() {
    const ctx = useContext(SignalRConnectionContext)
    // console.log("AL ", ctx);
    if (!ctx.clientData)
        return <span>loading</span>
    const handleToggle = (value: ActionData) => (e: React.MouseEvent<HTMLButtonElement>) => {
        if (e.ctrlKey) {
            ctx.clientData.actions = ctx.clientData.actions.map(a => a.id === value.id ? {
                ...a,
                hidden: !value.hidden
            } : {...a, hidden: value.hidden});
            ctx.setClientData({...ctx.clientData})
            return;
        }
        value.hidden = !value.hidden;
        ctx.setClientData({...ctx.clientData})
    };
    return <>
        <Divider/>
        <Typography gutterBottom mt={2}
                    variant="h5">Actions</Typography>
        <TableContainer sx={{overflow: 'auto'}}>
            <Table sx={{overflow: "auto"}} size="small">
                <TableBody>
                    {ctx.clientData.actions.map(a => {
                        return <TableRow key={a.id}>
                            <TableCell>
                                <Switch size="small" checked={!a.hidden} onClick={handleToggle(a)}/>
                                <span>
                            {a.name}
                                </span>
                            </TableCell>
                        </TableRow>
                    })}
                </TableBody>
            </Table>
        </TableContainer>
    </>
}
