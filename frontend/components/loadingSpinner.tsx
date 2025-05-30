'use client'

import React from "react"
import { Hourglass } from "lucide-react"

interface LoadingSpinnerProps {
  /** size in pixels */
  size?: number
  /** background opacity 0-100 */
  backdropOpacity?: number
}

export function LoadingSpinner({ size = 64, backdropOpacity = 75 }: LoadingSpinnerProps) {
  return (
    <div
      className={`fixed inset-0 flex items-center justify-center bg-white bg-opacity-${backdropOpacity} z-50`}
      role="status"
      aria-label="Loading page"
    >
      <Hourglass
        size={size}
        className="animate-spin text-pink-500"
      />
    </div>
  )
}

export default LoadingSpinner
