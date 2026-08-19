import { BrowserRouter, Routes, Route } from "react-router-dom";

import ProtectedRoute from "../auth/ProtectedRoute";
import MainLayout from "../layouts/MainLayout";
import DashboardPage from "../pages/DashboardPage";
import ImportPage from "../pages/ImportPage";
import LoginPage from "../pages/LoginPage";
import PortfolioSnapshotsPage from "../pages/PortfolioSnapshotsPage";

export default function AppRouter() {
    return (
        <BrowserRouter>

            <Routes>
                <Route path="/login" element={<LoginPage />} />

                <Route element={<ProtectedRoute />}>
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
                </Route>

            </Routes>

        </BrowserRouter>
    );
}
