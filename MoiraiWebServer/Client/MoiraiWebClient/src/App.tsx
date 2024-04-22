import './App.css'
import {useMoiraiStore} from "./SignalRConnection.tsx";
import {Route, Routes} from 'react-router-dom';
import {RecordList} from "./RecordList.tsx";
import {EventList} from "./EventList.tsx";
import {EntityDetails} from "./EntityDetails.tsx";
import Box from '@mui/material/Box';
import {
    AppBar,
    Grid,
    Stack,
    Toolbar,
    Typography,
    Button,
    CircularProgress,
    Backdrop, Tabs, Tab, CircularProgressProps,
} from "@mui/material";
import {useEffect, useState} from 'react';
import { ChangesetList } from './ChangesetList.tsx';
import {useMainListDisplay, useYearsDelta} from "./utils.tsx";
import {QueryView} from "./QueryView.tsx";
import {IStreamSubscriber} from "@microsoft/signalr";
import {ChartView} from "./ChartView.tsx";
interface TabPanelProps {
    children?: React.ReactNode;
    index: number;
    value: number;
}
function CustomTabPanel(props: TabPanelProps) {
    const { children, value, index, ...other } = props;
    return (
        <div
            role="tabpanel"
            hidden={value !== index}
            id={`simple-tabpanel-${index}`}
            aria-labelledby={`simple-tab-${index}`}
            style={{height: "100%"}}
            {...other}
        >
            {value === index && (
                    children
            )}
        </div>
    );
}

function CircularProgressWithLabel(
    props: CircularProgressProps & { value: number },
) {
    return (
        <Box sx={{ position: 'relative', display: 'inline-flex' }}>
            <CircularProgress variant="determinate" {...props} color="inherit" />
            <Box
                sx={{
                    top: 0,
                    left: 0,
                    bottom: 0,
                    right: 0,
                    position: 'absolute',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                }}
            >
                <Typography
                    variant="caption"
                    component="div"
                >{`${Math.round(props.value)}%`}</Typography>
            </Box>
        </Box>
    );
}

function InnerApp() {
    const conn = useMoiraiStore(s => s.conn!);
    const year = useMoiraiStore(s => s.year);
    const [mainListDisplay, setMainListDisplay] = useMainListDisplay();
    const [yearsDelta,_setYearsDelta] = useYearsDelta();
    const [progress,setProgress] = useState<number|undefined>(undefined);
    const subscriber: IStreamSubscriber<number> = {
        next(value: number) {
            setProgress(value)
        },
        error(err: any) {
            console.error(err)
            setProgress(undefined)
        },
        complete() {
            console.log("PROGRESS COMPLETE")
            setProgress(undefined)
        }
    };

    const passYearsProgress = () => {

        setProgress(0)
        return conn.passYears(yearsDelta).subscribe(subscriber);
    };
    // useEffect(()=> {
    //     setYearsDelta(100);
    // }, []);
    return <>
        {/*<Box>*/}
            <AppBar  position="relative" sx={{marginBottom:"12px"}}>
                <Toolbar variant={"dense"}>
                    <Tabs value={mainListDisplay} onChange={(_e,v) => setMainListDisplay(v)}
                        indicatorColor="secondary"
                        textColor="inherit">
                        <Tab label="Records"/>
                        <Tab label="Changesets"/>
                        <Tab label="Query"/>
                        <Tab label="Charts"/>
                    </Tabs>
                   
                    <Typography variant="h6" component="div" sx={{flexGrow: 1}}/>
                    {progress && <CircularProgressWithLabel sx={{marginRight: 2}} value={progress || 0}/>}
                    <Typography color="inherit" >
                        Year: {year}
                    </Typography>
                    <Button color="inherit" disabled={!!progress} onClick={passYearsProgress}>Pass {yearsDelta} years</Button>
                    <Button color="inherit" onClick={() => conn.save()}>Save</Button>
                    <Button color="inherit" onClick={() => {
                        conn.reset();
                    }}>Reset</Button>
                </Toolbar>
            </AppBar>
        {/*</Box>*/}
        <Grid container spacing={2} sx={{flexGrow: 1, height: "100%"}} mb={8} px={2}>
            <Grid item xs={4} sx={{height:"100%"}}>
                <Stack gap={2} sx={{height:"95%"}} direction={"column"} >
                    <Box>
                        <EntityDetails/>
                    </Box>
                    <Box  sx={{overflow:"auto"}}>
                        <EventList/>
                    </Box>
                </Stack>
            </Grid>
            <Grid item xs={8} sx={{paddingBottom:"64px", height: "100%"}}>
                <CustomTabPanel value={mainListDisplay} index={0}>
                    <RecordList/>
                </CustomTabPanel>
                <CustomTabPanel value={mainListDisplay} index={1}>
                    <ChangesetList/>
                </CustomTabPanel>
                <CustomTabPanel value={mainListDisplay} index={2}>
                   <QueryView/>
                </CustomTabPanel>
                <CustomTabPanel value={mainListDisplay} index={3}>
                   <ChartView/>
                </CustomTabPanel>
            </Grid>
        </Grid>
    </>
}

function App() {
    // const [clientData, setClientData] = useState<ClientData>();
    // const [conn, setConn] = useState<SignalRConnection | null>(null);
    // const [records, setRecords] = useState<Record[]>([]);

    // useEffect(() => {
    //     if (!conn)
    //         SignalRConnection.make().then(([conn,data]) => {setConn(conn); setClientData(data)})
    // }, [conn]);
    
    const connected = useMoiraiStore(s => s.connected);
    const handleKeyPress = useMoiraiStore(s => s.handleKeyPress);
    useEffect(() => {
        window.addEventListener("keydown", handleKeyPress);

        return () => window.removeEventListener("keydown", handleKeyPress);
    }, []);
    return connected ? (
                <Routes>
                    <Route path="/" element={<InnerApp/>}/>
                </Routes>

        ) :
        <Backdrop
            sx={{ color: '#fff', zIndex: (theme) => theme.zIndex.drawer + 1 }}
            open={true}
        >
            <Stack sx={{alignItems:"center"}} gap={4}>
                <img width="64px" src="/icon.png" />
                <CircularProgress color="inherit" />
            </Stack>
        </Backdrop>
}

export default App
