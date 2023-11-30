import {
    Divider,
    Switch,
    TableRow,
    Typography
} from "@mui/material";
import {useMoiraiStore} from "./SignalRConnection.tsx";
import Table from "@mui/material/Table";
import TableContainer from "@mui/material/TableContainer";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import {ActionData} from "./types.ts";

export function EventList() {
    const clientData = useMoiraiStore(s=>s.clientData)
    const toggleActionFiltering = useMoiraiStore(s=>s.toggleActionFiltering)
    // console.log("AL ", ctx);
    if (!clientData)
        return <span>loading</span>
    const handleToggle = (value: ActionData) => (e: React.MouseEvent<HTMLButtonElement>) => {
        toggleActionFiltering(value.id, value.hidden, e.ctrlKey)
        // if (e.ctrlKey) {
        //     clientData.actions = clientData.actions.map(a => a.id === value.id ? {
        //         ...a,
        //         hidden: !value.hidden
        //     } : {...a, hidden: value.hidden});
        //     setClientData({...clientData})
        //     return;
        // }
        // value.hidden = !value.hidden;
        // setClientData({...clientData})
    };
    return <>
        <Divider/>
        <Typography gutterBottom mt={2}
                    variant="h5">Actions</Typography>
        <TableContainer sx={{overflow: 'auto'}}>
            <Table sx={{overflow: "auto"}} size="small">
                <TableBody>
                    {clientData.actions.map(a => {
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
