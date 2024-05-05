import { Divider, Switch, TableRow, Typography } from "@mui/material";
import { useMoiraiStore } from "./SignalRConnection.tsx";
import Table from "@mui/material/Table";
import TableContainer from "@mui/material/TableContainer";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import IconButton from "@mui/material/IconButton";
import { ActionData } from "./types.ts";

import PlayArrow from "@mui/icons-material/PlayArrow";
export function EventList() {
  const clientData = useMoiraiStore((s) => s.clientData);
  const toggleActionFiltering = useMoiraiStore((s) => s.toggleActionFiltering);
  const conn = useMoiraiStore((s) => s.conn!);
  if (!clientData) return <span>loading</span>;
//   console.log(clientData);
  const handleToggle =
    (value: ActionData) => (e: React.MouseEvent<HTMLButtonElement>) => {
      toggleActionFiltering(value.id, value.hidden, e.ctrlKey);
    };
  return (
    <>
      <Divider />
      <Typography gutterBottom mt={2} variant="h5">
        Actions
      </Typography>
      <TableContainer sx={{ overflow: "auto" }}>
        <Table sx={{ overflow: "auto" }} size="small">
          <TableBody>
            {clientData.actions?.map((a) => {
              return (
                <TableRow key={a.id}>
                  <TableCell>
                    <Switch
                      size="small"
                      checked={!a.hidden}
                      onClick={handleToggle(a)}
                    />
                    <span>{a.name}</span>
                  </TableCell>
                  <TableCell>
                    <IconButton onClick={() => conn.runAction(a.id)}>
                      <PlayArrow />
                    </IconButton>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </TableContainer>
    </>
  );
}
