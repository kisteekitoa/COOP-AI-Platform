import { Outlet } from "react-router-dom";
import Sidebar from "../components/layout/Sidebar";

export default function MainLayout() {
    return (
        <div className="flex min-h-screen bg-slate-100">

            <Sidebar />

            <main className="flex-1">

                <header className="h-16 bg-white border-b shadow-sm flex items-center px-8">

                    <div>

                        <h2 className="text-2xl font-semibold text-slate-800">
                            Dashboard
                        </h2>

                        <p className="text-sm text-slate-500">
                            COOP-AI Smart Cooperative Platform
                        </p>

                    </div>

                </header>

                <section className="p-8">
                    <Outlet />
                </section>

            </main>

        </div>
    );
}