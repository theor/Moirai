import './App.css'
import {useContext, useEffect, useState} from "react";
import Button from '@mui/material/Button';
import {SignalRConnection, SignalRConnectionContext} from "./SignalRConnection.tsx";
import {Grid, Stack} from "@mui/material";
import {Outlet, Route, Routes} from 'react-router-dom';
import {RecordList} from "./RecordList.tsx";
import {EntityDetails} from "./EntityDetails.tsx";

function InnerApp() {
    const conn = useContext(SignalRConnectionContext);
    return <Grid container height="100vh"  pt={2} pb={2}>
        <Grid item xs={4} p={2}>
            <Outlet/>
        </Grid>
        <Grid item xs={8} p={2}>
            <Stack spacing={2} sx={{height: "100%"}}>
                <Stack direction="row" spacing={1}>
                    <Button variant="contained" onClick={() => conn.passYears(100)}>Pass years</Button>
                    <Button variant="outlined" onClick={() => conn.save()}>Save</Button>
                    <Button variant={"outlined"} onClick={() => {
                        conn.reset();
                    }}>Reset</Button>
                </Stack>
                <RecordList  />
            </Stack>
        </Grid>
    </Grid>
}
function App() {
    const [conn, setConn] = useState<SignalRConnection | null>(null);

    useEffect(() => {
        if (!conn)
            SignalRConnection.make().then(setConn)
    }, [conn]);

    return conn ? (
            <SignalRConnectionContext.Provider value={conn}>
            <Routes>
                <Route path="/" element={<InnerApp/>}>
                    <Route path="entity/:eid" element={<EntityDetails  /> }/>
                </Route>
            </Routes>
               
            </SignalRConnectionContext.Provider>
        ) :
        <span>Loading</span>
}

export default App
