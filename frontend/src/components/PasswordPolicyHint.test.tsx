import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import PasswordPolicyHint from './PasswordPolicyHint'
import type { PasswordPolicyResponse } from '../api/types'

const basePolicy: PasswordPolicyResponse = {
  minLength: 10,
  requireUppercase: true,
  requireLowercase: true,
  requireDigit: true,
  requireNonAlphanumeric: true,
  requirements: [],
}

describe('PasswordPolicyHint', () => {
  it('policy 为 null 时不渲染', () => {
    const { container } = render(<PasswordPolicyHint policy={null} />)
    expect(container.firstChild).toBeNull()
  })

  it('展示所有策略要求', () => {
    render(<PasswordPolicyHint policy={basePolicy} />)
    expect(screen.getByText('至少 10 个字符')).toBeInTheDocument()
    expect(screen.getByText('至少一个大写字母 (A-Z)')).toBeInTheDocument()
    expect(screen.getByText('至少一个小写字母 (a-z)')).toBeInTheDocument()
    expect(screen.getByText('至少一个数字 (0-9)')).toBeInTheDocument()
    expect(screen.getByText('至少一个特殊字符 (!@#$%^&*...)')).toBeInTheDocument()
  })

  it('未传 password 时不显示校验状态图标', () => {
    const { container } = render(<PasswordPolicyHint policy={basePolicy} />)
    // 未传 password 时不应渲染 svg 图标
    expect(container.querySelectorAll('svg')).toHaveLength(0)
  })

  it('传入符合策略的 password 时所有项为绿色', () => {
    const { container } = render(
      <PasswordPolicyHint policy={basePolicy} password="StrongP@ss1" />
    )
    const items = container.querySelectorAll('li')
    expect(items).toHaveLength(5)
    items.forEach((item) => {
      expect(item.className).toContain('text-emerald-600')
    })
  })

  it('传入不符合策略的 password 时对应项为红色', () => {
    const { container } = render(
      <PasswordPolicyHint policy={basePolicy} password="weak" />
    )
    const items = container.querySelectorAll('li')
    // "weak" 长度不足、无大写、无数字、无特殊字符，只有小写字母通过
    expect(items[0].className).toContain('text-red-500') // 长度不足
    expect(items[1].className).toContain('text-red-500') // 无大写
    expect(items[2].className).not.toContain('text-red-500') // 有小写，通过
    expect(items[3].className).toContain('text-red-500') // 无数字
    expect(items[4].className).toContain('text-red-500') // 无特殊字符
  })

  it('空字符串 password 时不显示校验状态', () => {
    const { container } = render(
      <PasswordPolicyHint policy={basePolicy} password="" />
    )
    expect(container.querySelectorAll('svg')).toHaveLength(0)
  })
})
