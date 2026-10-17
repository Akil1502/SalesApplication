import { createContext, useContext } from 'react'

export interface TourControls {
  /** Starts the tour from the beginning (on the dashboard). */
  start: () => void
}

export const TourContext = createContext<TourControls>({ start: () => {} })

export const useTour = () => useContext(TourContext)
