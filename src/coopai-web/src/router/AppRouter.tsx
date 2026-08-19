import { BrowserRouter, Routes, Route } from "react-router-dom";

import MainLayout from "../layouts/MainLayout";
import DashboardPage from "../pages/DashboardPage";
import ImportPage from "../pages/ImportPage";
import PortfolioSnapshotsPage from "../pages/PortfolioSnapshotsPage";

export default function AppRouter() {
    return (
        <BrowserRouter>

            <Routes>

                <Route element={<MainLayout />}>

                    <Route
                        path="/"
                        element={<DashboardPage />}
                    />
                    <Route
                        path="/import"
                        element={<ImportPage />}
                    />
                    <Route
                        path="/portfolio-snapshots"
                        element={<PortfolioSnapshotsPage />}
                    />
                    <Route
                        path="/portfolio-snapshots/:id"
                        element={<PortfolioSnapshotsPage />}
                    />

                </Route>

            </Routes>

        </BrowserRouter>
    );
}
