"use client"

import Link from "next/link"
import { PiggyLogo } from "./piggy-logo"
import { Button } from "@/components/ui/button"
import { LayoutDashboard, BarChart3, LogOut, Menu, X, Sun } from "lucide-react"
import { type ReactNode, useState } from "react"
import { cn } from "@/lib/utils"
import { ToastContainer, toast } from 'react-toastify';
import { useAuth } from "./providers/auth-provider"
import { useMe } from "@/hooks/useMe"
import { usePathname } from "next/navigation"
import { ProtectedRoute } from "./protectedRoute"
interface DashboardLayoutProps {
  children: ReactNode
}

export function DashboardLayout({ children }: DashboardLayoutProps) {
  const {  logout } = useAuth();
  const { data: me, isLoading: meLoading, error: meError } = useMe();
  const [sidebarOpen, setSidebarOpen] = useState(false)

  const pathname = usePathname();

  const links = [
    { href: "/dashboard", label: "Dashboard", Icon: LayoutDashboard },
    { href: "/transactions", label: "Transactions", Icon: BarChart3 },
  ];
  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    console.log('hi')
     logout()

    
  }
  return (

    <div className="min-h-screen flex flex-col md:flex-row">
      {/* Mobile sidebar toggle */}
      <div className="md:hidden fixed top-4 left-4 z-50">
        <Button variant="outline" size="icon" onClick={() => setSidebarOpen(!sidebarOpen)} className="rounded-full">
          {sidebarOpen ? <X size={18} /> : <Menu size={18} />}
        </Button>
      </div>

      {/* Sidebar */}
      <div
        className={cn(
          "fixed inset-y-0 left-0 z-40 w-64 bg-white border-r transform transition-transform duration-200 ease-in-out md:relative md:translate-x-0",
          sidebarOpen ? "translate-x-0" : "-translate-x-full",
        )}
      >
        <div className="flex flex-col h-full">
          <div className="p-4 border-b">
            <div className="flex items-center gap-2">
              <PiggyLogo className="h-8 w-8 text-pink-500" />
              <span className="font-bold text-xl">PiggyPal</span>
            </div>
          </div>

          <nav className="flex-1 p-4 space-y-1">
          <div  className="flex items-center space-x-3 px-3 py-2 text-gray-700 ">
          

      <div className="space-x-1 text-xl">
       <span className="text-gray-500 font-bold">Hi</span> 
          <span className="text-pink-300 font-extrabold">{me?.data.username}</span>
          </div>
          </div>
      
          {links.map(({ href, label, Icon }) => {
        const isActive = pathname === href;
        return (
          <Link
            key={href}
            href={href}
            className={`
              flex items-center gap-3 px-3 py-2 font-medium rounded-md
              ${isActive
                ? "bg-pink-50 text-pink-600"   /* active: pink bg, gray text */
                : "text-gray-700"
              }
            `}
          >
            <Icon size={18} />
            <span>{label}</span>
          </Link>
        );
      })}

       
          </nav>

          <div className="p-4 border-t">
            <Button onClick={handleSubmit} variant="ghost" className="w-full justify-start text-gray-700 hover:bg-pink-50 hover:text-pink-600">
             
              <LogOut size={18} className="mr-2" />
              <span>Logout</span>
            </Button>
      
          </div>
        </div>
      </div>

      {/* Main content */}
      <div className="flex-1 md:ml-0">
        {/* Overlay for mobile sidebar */}
        {sidebarOpen && (
          <div className="fixed inset-0 bg-black/20 z-30 md:hidden" onClick={() => setSidebarOpen(false)} />
        )}

        <main className="min-h-screen">{children}</main>
      </div>
    </div>

  )
}
