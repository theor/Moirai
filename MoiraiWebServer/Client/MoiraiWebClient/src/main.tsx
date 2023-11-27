import ReactDOM from 'react-dom/client'
import App from './App.tsx'
import './index.css'
import { ThemeProvider } from '@emotion/react';
import { CssBaseline } from '@mui/material';
import theme from './theme';
import {createTheme} from "@mui/material/styles";

const defaultTheme = createTheme();

ReactDOM.createRoot(document.getElementById('root')!).render(
  // <React.StrictMode>
    <ThemeProvider theme={defaultTheme}>
        <CssBaseline />
        <App />
    </ThemeProvider>
  // </React.StrictMode>,
)
